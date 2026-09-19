using System.Security.Claims;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Endpoints;

public static class ImageEndpoints
{
    public static RouteGroupBuilder MapImageEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Viewer);

        // An image runs on every machine it is deployed to, so only an administrator changes the library.
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapGroup("/uploads").MapImageUploadEndpoints();

        return group;
    }

    // A library holds tens of images, and sorting here gives every database the same order.
    private static async Task<Ok<IReadOnlyList<ImageSummary>>> ListAsync(DdtDbContext database, CancellationToken cancellationToken)
    {
        List<Image> images = await database.Images.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<ImageSummary>>(
        [
            .. images
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.WimIndex)
                .ThenBy(i => i.Id)
                .Select(ImageSummaries.From),
        ]);
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(
        Guid id,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        ImageStore store,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // Under the library lock, so an upload of the same file cannot add rows for the stored file while it goes.
        await store.LibraryLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Image? image = await database.Images.FirstOrDefaultAsync(i => i.Id == id, cancellationToken).ConfigureAwait(false);

            if (image is null)
            {
                return TypedResults.NotFound();
            }

            bool inUse = await database.Deployments
                .AnyAsync(
                    d => d.ImageId == id && (d.State == DeploymentState.Assigned || d.State == DeploymentState.Running),
                    cancellationToken)
                .ConfigureAwait(false);

            if (inUse)
            {
                return TypedResults.Problem(
                    title: "Machines are waiting for this image or installing it. Cancel those deployments or let them finish, then delete it.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            database.Images.Remove(image);
            database.AuditEvents.Add(new AuditEvent
            {
                OccurredUtc = timeProvider.GetUtcNow(),
                Action = AuditActions.ImageDeleted,
                ActorUserId = Principals.UserId(user),
                ActorName = user.Identity?.Name,
                SubjectId = image.Id.ToString("D"),
                SourceAddress = context.Connection.RemoteIpAddress?.ToString(),
                Detail = $"{image.Name}, index {image.WimIndex}, SHA-256 {image.Sha256}.",
            });

            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // The row is gone, so the stored file goes too unless another index of the same file still uses it.
            await store.DeleteObjectIfUnreferencedAsync(database, image.Sha256, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            store.LibraryLock.Release();
        }

        live.ImagesChanged();

        return TypedResults.NoContent();
    }
}
