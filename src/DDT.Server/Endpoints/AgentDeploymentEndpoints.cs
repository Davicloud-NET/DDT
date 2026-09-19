using System.Globalization;
using System.Security.Claims;
using System.Text;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace DDT.Server.Endpoints;

// What an authorized machine needs to deploy itself: the images it may choose, its deployment, the image
// content and its answer file. All of it takes a session token, and the machine's own current generation.
public static class AgentDeploymentEndpoints
{
    private const int MaxAttempts = 3;

    public static RouteGroupBuilder MapAgentDeploymentEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/{id:guid}/images", ListImagesAsync)
            .RequireAuthorization(DdtPolicies.Machine)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine);

        // HEAD explicitly: the agent checks the image before it erases the disk, and a HEAD no endpoint matches
        // would fall through to the web UI's index page with 200.
        group.MapMethods("/{id:guid}/images/{sha256}", [HttpMethods.Get, HttpMethods.Head], ReadImageAsync)
            .RequireAuthorization(DdtPolicies.Machine)
            .RequireRateLimiting(RateLimitPolicies.AgentImage);

        group.MapPost("/{id:guid}/deployments", PickAsync)
            .RequireAuthorization(DdtPolicies.Machine)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine)
            .WithMetadata(new RequestSizeLimitAttribute(DeploymentLimits.MaxRequestBytes));

        group.MapPost("/{id:guid}/deployments/{deploymentId:guid}/report", ReportAsync)
            .RequireAuthorization(DdtPolicies.Machine)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine)
            .WithMetadata(new RequestSizeLimitAttribute(DeploymentLimits.MaxRequestBytes));

        group.MapGet("/{id:guid}/deployments/{deploymentId:guid}/unattend", ReadUnattendAsync)
            .RequireAuthorization(DdtPolicies.Machine)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine);

        return group;
    }

    private static async Task<Results<Ok<IReadOnlyList<AgentImageChoice>>, ForbidHttpResult, UnauthorizedHttpResult, NotFound, ProblemHttpResult>> ListImagesAsync(
        Guid id,
        ClaimsPrincipal user,
        DdtDbContext database,
        DeploymentService deployments,
        CancellationToken cancellationToken)
    {
        if (!Principals.IsMachine(user, id))
        {
            return TypedResults.Forbid(authenticationSchemes: [DdtAuthenticationSchemes.Machine]);
        }

        Machine? machine = await database.Machines.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);

        if (machine is null)
        {
            return TypedResults.NotFound();
        }

        if (!Principals.HoldsCurrentGeneration(user, machine))
        {
            return TypedResults.Unauthorized();
        }

        if (!await deployments.CanPickImageAsync(machine, cancellationToken).ConfigureAwait(false))
        {
            return TypedResults.Problem(
                title: "Only an operator or administrator signed in at this machine can choose an image. Sign in at the machine first.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        IReadOnlyList<Image> images = await deployments.DeployableImagesAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<AgentImageChoice>>(
        [
            .. images.Select(i => new AgentImageChoice(i.Id, i.Name, i.Edition, i.Language, i.SizeBytes, i.InstalledBytes)),
        ]);
    }

    private static async Task<Results<Ok<AgentDeployment>, ForbidHttpResult, UnauthorizedHttpResult, NotFound, ProblemHttpResult, ValidationProblem>> PickAsync(
        Guid id,
        AgentPickRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        DeploymentService deployments,
        ImageStore store,
        LiveNotifier live,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!Principals.IsMachine(user, id))
        {
            return TypedResults.Forbid(authenticationSchemes: [DdtAuthenticationSchemes.Machine]);
        }

        Machine? machine = await database.Machines.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);

        if (machine is null)
        {
            return TypedResults.NotFound();
        }

        if (!Principals.HoldsCurrentGeneration(user, machine))
        {
            return TypedResults.Unauthorized();
        }

        Deployment deployment;

        // Under the library lock, so the image cannot be deleted between its lookup and the saved deployment.
        await store.LibraryLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            DeploymentDecision decision = await deployments
                .PickAsync(machine, request, context.Connection.RemoteIpAddress?.ToString(), cancellationToken)
                .ConfigureAwait(false);

            switch (decision.Outcome)
            {
                case DeploymentOutcome.NotFound:
                    return TypedResults.Problem(title: decision.Reason, statusCode: StatusCodes.Status404NotFound);
                case DeploymentOutcome.Conflict:
                    return TypedResults.Problem(title: decision.Reason, statusCode: StatusCodes.Status409Conflict);
                case DeploymentOutcome.Invalid:
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [decision.Field!] = [decision.Reason!] });
            }

            deployment = decision.Deployment!;

            try
            {
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                return TypedResults.Problem(
                    title: "The machine changed while the image was chosen. Choose the image again.",
                    statusCode: StatusCodes.Status409Conflict);
            }
        }
        finally
        {
            store.LibraryLock.Release();
        }

        DeploymentLog.Changed(loggerFactory.CreateLogger(typeof(AgentDeploymentEndpoints)), deployment, null);
        live.MachineChanged(machine, deployment);

        return TypedResults.Ok(DeploymentSummaries.ForAgent(deployment));
    }

    // A running deployment does not poll next, so every report refreshes last seen and hands out the tokens a
    // poll would.
    private static async Task<Results<Ok<AgentDeploymentReportResult>, ForbidHttpResult, UnauthorizedHttpResult, NotFound, ProblemHttpResult, ValidationProblem>> ReportAsync(
        Guid id,
        Guid deploymentId,
        AgentDeploymentReport report,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        DeploymentService deployments,
        MachineRegistrar registrar,
        MachineTokenService tokens,
        LiveNotifier live,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!Principals.IsMachine(user, id))
        {
            return TypedResults.Forbid(authenticationSchemes: [DdtAuthenticationSchemes.Machine]);
        }

        string? address = context.Connection.RemoteIpAddress?.ToString();

        for (int attempt = 1; ; attempt++)
        {
            Machine? machine = await database.Machines.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);

            if (machine is null)
            {
                return TypedResults.NotFound();
            }

            // Stopped, rejected or registered again since the token was checked: the run is over for this agent.
            if (!Principals.HoldsCurrentGeneration(user, machine))
            {
                return TypedResults.Unauthorized();
            }

            Deployment? known = await database.Deployments.FindAsync([deploymentId], cancellationToken).ConfigureAwait(false);
            DeploymentState? before = known?.State;

            DeploymentDecision decision = await deployments
                .ReportAsync(machine, deploymentId, report, address, cancellationToken)
                .ConfigureAwait(false);

            switch (decision.Outcome)
            {
                case DeploymentOutcome.NotFound:
                    return TypedResults.Problem(title: decision.Reason, statusCode: StatusCodes.Status404NotFound);
                case DeploymentOutcome.Conflict:
                    return TypedResults.Problem(title: decision.Reason, statusCode: StatusCodes.Status409Conflict);
                case DeploymentOutcome.Invalid:
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [decision.Field!] = [decision.Reason!] });
                case DeploymentOutcome.Unchanged:
                    return TypedResults.Ok(Tokens(machine, registrar, tokens));
            }

            Deployment deployment = decision.Deployment!;
            LastSeen.Record(machine, timeProvider.GetUtcNow(), address);

            try
            {
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                // Decide again from what is stored now. A stop or a new registration bumped the generation, and
                // the next attempt answers 401.
                database.ChangeTracker.Clear();

                continue;
            }

            DeploymentLog.Changed(loggerFactory.CreateLogger(typeof(AgentDeploymentEndpoints)), deployment, before);
            live.MachineChanged(machine, deployment);

            return TypedResults.Ok(Tokens(machine, registrar, tokens));
        }
    }

    private static async Task<Results<ContentHttpResult, ForbidHttpResult, UnauthorizedHttpResult, NotFound, ProblemHttpResult>> ReadUnattendAsync(
        Guid id,
        Guid deploymentId,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        DeploymentService deployments,
        UnattendRenderer renderer,
        CancellationToken cancellationToken)
    {
        if (!Principals.IsMachine(user, id))
        {
            return TypedResults.Forbid(authenticationSchemes: [DdtAuthenticationSchemes.Machine]);
        }

        Machine? machine = await database.Machines.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);

        if (machine is null)
        {
            return TypedResults.NotFound();
        }

        if (!Principals.HoldsCurrentGeneration(user, machine))
        {
            return TypedResults.Unauthorized();
        }

        if (await deployments.RunningAsync(machine, deploymentId, cancellationToken).ConfigureAwait(false) is not { } deployment)
        {
            return TypedResults.Problem(
                title: "Only a running deployment of this machine gets its answer file. Report the deployment as running first.",
                statusCode: StatusCodes.Status409Conflict);
        }

        Image? image = deployment.ImageId is { } imageId
            ? await database.Images.FindAsync([imageId], cancellationToken).ConfigureAwait(false)
            : null;

        // It holds the deployment passwords.
        context.Response.Headers.CacheControl = "no-store";

        return TypedResults.Text(renderer.Render(machine, image), "application/xml", Encoding.UTF8);
    }

    // Content addressed: the tag is the hash, so a resumed range can never splice two different files.
    private static async Task<Results<PhysicalFileHttpResult, ForbidHttpResult, UnauthorizedHttpResult, NotFound, ProblemHttpResult>> ReadImageAsync(
        Guid id,
        string sha256,
        ClaimsPrincipal user,
        DdtDbContext database,
        DeploymentService deployments,
        ImageStore store,
        CancellationToken cancellationToken)
    {
        if (!Principals.IsMachine(user, id))
        {
            return TypedResults.Forbid(authenticationSchemes: [DdtAuthenticationSchemes.Machine]);
        }

        Machine? machine = await database.Machines.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);

        if (machine is null)
        {
            return TypedResults.NotFound();
        }

        if (!Principals.HoldsCurrentGeneration(user, machine))
        {
            return TypedResults.Unauthorized();
        }

        Deployment? active = await deployments.ActiveAsync(machine, cancellationToken).ConfigureAwait(false);

        if (active is null || !string.Equals(active.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Problem(
                title: "This machine has no deployment of that image. Ask the server for the current deployment.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        // The stored hash, never the one in the URL, names the file.
        string path = store.ObjectPath(active.Sha256);

        if (!File.Exists(path))
        {
            return TypedResults.Problem(
                title: "The image file is missing from the server's library. Upload the image again and assign it anew.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.PhysicalFile(
            path,
            "application/octet-stream",
            entityTag: new EntityTagHeaderValue(string.Create(CultureInfo.InvariantCulture, $"\"{active.Sha256}\"")),
            enableRangeProcessing: true);
    }

    private static AgentDeploymentReportResult Tokens(Machine machine, MachineRegistrar registrar, MachineTokenService tokens) =>
        new(registrar.CurrentToken(machine), tokens.Issue(machine, MachineTokenPurpose.Resume));
}
