using System.Security.Claims;
using DDT.Contracts.Machines;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Endpoints;

public static class EnrollmentTokenEndpoints
{
    private const int MaxNameLength = 64;
    private const int MaxValidForDays = 90;

    public static RouteGroupBuilder MapEnrollmentTokenEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/", CreateAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/{id:guid}/revoke", RevokeAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static async Task<Ok<IReadOnlyList<EnrollmentTokenSummary>>> ListAsync(DdtDbContext database, CancellationToken cancellationToken)
    {
        List<EnrollmentToken> tokens = await database.EnrollmentTokens.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<EnrollmentTokenSummary>>(
        [
            .. tokens.OrderByDescending(t => t.CreatedUtc).Select(Summary),
        ]);
    }

    private static async Task<Results<Ok<CreatedEnrollmentToken>, ValidationProblem>> CreateAsync(
        CreateEnrollmentTokenRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        EnrollmentTokenService enrollmentTokens,
        DdtDbContext database,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        string name = request.Name?.Trim() ?? string.Empty;

        if (name.Length is 0 or > MaxNameLength)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = [$"A name of 1 to {MaxNameLength} characters is required."],
            });
        }

        if (request.ValidForDays is < 1 or > MaxValidForDays)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["validForDays"] = [$"Tokens are valid for 1 to {MaxValidForDays} days."],
            });
        }

        Guid? userId = Principals.UserId(user);

        (EnrollmentToken record, string token) = await enrollmentTokens
            .CreateAsync(name, TimeSpan.FromDays(request.ValidForDays), userId, cancellationToken)
            .ConfigureAwait(false);

        database.AuditEvents.Add(new AuditEvent
        {
            OccurredUtc = timeProvider.GetUtcNow(),
            Action = AuditActions.EnrollmentTokenCreated,
            ActorUserId = userId,
            ActorName = user.Identity?.Name,
            SubjectId = record.Id.ToString("N"),
            SourceAddress = context.Connection.RemoteIpAddress?.ToString(),
            Detail = $"{record.Name}, expires {record.ExpiresUtc:O}.",
        });

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new CreatedEnrollmentToken(Summary(record), token));
    }

    private static async Task<Results<Ok<EnrollmentTokenSummary>, NotFound>> RevokeAsync(
        Guid id,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        EnrollmentToken? record = await database.EnrollmentTokens
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            return TypedResults.NotFound();
        }

        if (record.RevokedUtc is null)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            record.RevokedUtc = now;

            database.AuditEvents.Add(new AuditEvent
            {
                OccurredUtc = now,
                Action = AuditActions.EnrollmentTokenRevoked,
                ActorUserId = Principals.UserId(user),
                ActorName = user.Identity?.Name,
                SubjectId = record.Id.ToString("N"),
                SourceAddress = context.Connection.RemoteIpAddress?.ToString(),
                Detail = record.Name,
            });

            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return TypedResults.Ok(Summary(record));
    }

    private static EnrollmentTokenSummary Summary(EnrollmentToken token) =>
        new(token.Id, token.Name, token.CreatedUtc, token.ExpiresUtc, token.RevokedUtc);
}
