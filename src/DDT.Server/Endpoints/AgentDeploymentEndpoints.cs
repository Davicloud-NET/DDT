// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

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
using DeploymentStep = DDT.Server.Deployments.DeploymentStep;

namespace DDT.Server.Endpoints;

// What an authorized machine needs to run a task sequence: the sequences it may choose, its run, the run's files and,
// just in time, its secrets. All of it takes a session token, and the machine's own current generation.
public static class AgentDeploymentEndpoints
{
    private const int MaxAttempts = 3;

    public static RouteGroupBuilder MapAgentDeploymentEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/{id:guid}/sequences", ListSequencesAsync)
            .RequireAuthorization(DdtPolicies.Machine)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine);

        group.MapPost("/{id:guid}/runs", PickAsync)
            .RequireAuthorization(DdtPolicies.Machine)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine)
            .WithMetadata(new RequestSizeLimitAttribute(DeploymentLimits.MaxRequestBytes));

        group.MapPost("/{id:guid}/runs/{runId:guid}/report", ReportAsync)
            .RequireAuthorization(DdtPolicies.Machine)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine)
            .WithMetadata(new RequestSizeLimitAttribute(DeploymentLimits.MaxReportBytes));

        // HEAD explicitly: the agent checks a file's size before it erases the disk, and a HEAD no endpoint matches
        // would fall through to the web UI's index page with 200.
        group.MapMethods("/{id:guid}/runs/{runId:guid}/files/{sha256}", [HttpMethods.Get, HttpMethods.Head], ReadFileAsync)
            .RequireAuthorization(DdtPolicies.Machine)
            .RequireRateLimiting(RateLimitPolicies.AgentImage);

        group.MapGet("/{id:guid}/runs/{runId:guid}/steps/{stepId:guid}/unattend", ReadAnswerFileAsync)
            .RequireAuthorization(DdtPolicies.Machine)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine);

        group.MapGet("/{id:guid}/runs/{runId:guid}/steps/{stepId:guid}/credentials", ReadJoinCredentialsAsync)
            .RequireAuthorization(DdtPolicies.Machine)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine);

        group.MapGet("/{id:guid}/runs/{runId:guid}/steps/{stepId:guid}/accounts", ReadStepAccountsAsync)
            .RequireAuthorization(DdtPolicies.Machine)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine);

        // An agent from before task sequences never gets an image deployment from this server, so it has no reason to
        // call these. Answered here rather than by the web UI's fallback page, which would answer a GET with 200.
        group.MapMethods("/{id:guid}/images/{**rest}", [HttpMethods.Get, HttpMethods.Head], Gone)
            .RequireAuthorization(DdtPolicies.Machine)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine);

        group.MapMethods("/{id:guid}/deployments/{**rest}", [HttpMethods.Get, HttpMethods.Head, HttpMethods.Post], Gone)
            .RequireAuthorization(DdtPolicies.Machine)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine);

        return group;
    }

    private static ProblemHttpResult Gone() =>
        TypedResults.Problem(
            title: "This server runs task sequences and no longer deploys images this way. Start the machine from the network again to update its agent.",
            statusCode: StatusCodes.Status410Gone);

    private static async Task<Results<Ok<IReadOnlyList<AgentSequenceChoice>>, ForbidHttpResult, UnauthorizedHttpResult, NotFound, ProblemHttpResult>> ListSequencesAsync(
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

        if (!await deployments.CanPickAsync(machine, cancellationToken).ConfigureAwait(false))
        {
            return TypedResults.Problem(
                title: "Only an operator or administrator signed in at this machine can choose a sequence. Sign in at the machine first.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        return TypedResults.Ok(await deployments.ChoicesAsync(machine, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<Results<Ok<AgentRun>, ForbidHttpResult, UnauthorizedHttpResult, NotFound, ProblemHttpResult, ValidationProblem>> PickAsync(
        Guid id,
        AgentRunRequest request,
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

        Deployment run;

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

            run = decision.Deployment!;

            try
            {
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                return TypedResults.Problem(
                    title: "The machine changed while the sequence was chosen. Choose the sequence again.",
                    statusCode: StatusCodes.Status409Conflict);
            }
        }
        finally
        {
            store.LibraryLock.Release();
        }

        DeploymentLog.Changed(loggerFactory.CreateLogger(typeof(AgentDeploymentEndpoints)), run, null);
        live.MachineChanged(machine, run);

        return TypedResults.Ok(await deployments.AgentRunAsync(machine, run, cancellationToken).ConfigureAwait(false));
    }

    // A running run does not poll next, so every report refreshes last seen and hands out the tokens a poll would.
    private static async Task<Results<Ok<AgentRunReportResult>, ForbidHttpResult, UnauthorizedHttpResult, NotFound, ProblemHttpResult, ValidationProblem>> ReportAsync(
        Guid id,
        Guid runId,
        AgentRunReport report,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        RunReports reports,
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

            DeploymentState? before = (await database.Deployments.FindAsync([runId], cancellationToken).ConfigureAwait(false))?.State;

            DeploymentDecision decision = await reports.ApplyAsync(machine, runId, report, address, cancellationToken).ConfigureAwait(false);

            switch (decision.Outcome)
            {
                case DeploymentOutcome.NotFound:
                    return TypedResults.Problem(title: decision.Reason, statusCode: StatusCodes.Status404NotFound);
                case DeploymentOutcome.Conflict:
                    return TypedResults.Problem(title: decision.Reason, statusCode: StatusCodes.Status409Conflict);
                case DeploymentOutcome.Invalid:
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [decision.Field!] = [decision.Reason!] });
                case DeploymentOutcome.Unchanged:
                    return TypedResults.Ok(Tokens(machine, decision.Deployment!, registrar, tokens));
            }

            Deployment run = decision.Deployment!;
            IReadOnlyList<DeploymentStep> changedSteps = RunReports.ChangedSteps(database);
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

            DeploymentLog.Changed(loggerFactory.CreateLogger(typeof(AgentDeploymentEndpoints)), run, before);
            live.MachineChanged(machine, run);
            live.RunStepsChanged(machine.Id, changedSteps);

            return decision.Outcome == DeploymentOutcome.Refused
                ? TypedResults.Problem(title: decision.Reason, statusCode: StatusCodes.Status409Conflict)
                : TypedResults.Ok(Tokens(machine, run, registrar, tokens));
        }
    }

    // Content addressed: the tag is the hash, so a resumed range can never splice two different files. Only the files
    // frozen with the machine's active run, so a machine never reads the library at large.
    private static async Task<Results<PhysicalFileHttpResult, ForbidHttpResult, UnauthorizedHttpResult, NotFound, ProblemHttpResult>> ReadFileAsync(
        Guid id,
        Guid runId,
        string sha256,
        ClaimsPrincipal user,
        DdtDbContext database,
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

        string hash = sha256.ToLowerInvariant();
        DeploymentArtifact? artifact = machine.ActiveDeploymentId == runId
            ? await database.DeploymentArtifacts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.DeploymentId == runId && a.Sha256 == hash, cancellationToken)
                .ConfigureAwait(false)
            : null;

        if (artifact is null)
        {
            return TypedResults.Problem(
                title: "This machine's run has no such file. Ask the server for the current run.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        // The stored hash, never the one in the URL, names the file.
        string path = store.ObjectPath(artifact.Sha256);

        if (!File.Exists(path))
        {
            return TypedResults.Problem(
                title: "The file is missing from the server's library. Upload it again and assign the sequence anew.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.PhysicalFile(
            path,
            "application/octet-stream",
            entityTag: new EntityTagHeaderValue(string.Create(CultureInfo.InvariantCulture, $"\"{artifact.Sha256}\"")),
            enableRangeProcessing: true);
    }

    private static async Task<Results<ContentHttpResult, ForbidHttpResult, UnauthorizedHttpResult, NotFound, ProblemHttpResult>> ReadAnswerFileAsync(
        Guid id,
        Guid runId,
        Guid stepId,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        RunSecrets secrets,
        CancellationToken cancellationToken)
    {
        if (await SecretMachineAsync(id, user, database, cancellationToken).ConfigureAwait(false) is not { } machine)
        {
            return Principals.IsMachine(user, id) ? TypedResults.Unauthorized() : TypedResults.Forbid(authenticationSchemes: [DdtAuthenticationSchemes.Machine]);
        }

        (string? answer, string? refusal) = await secrets
            .AnswerFileAsync(machine, runId, stepId, context.Connection.RemoteIpAddress?.ToString(), cancellationToken)
            .ConfigureAwait(false);

        if (refusal is not null)
        {
            return TypedResults.Problem(title: refusal, statusCode: StatusCodes.Status409Conflict);
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Response.Headers.CacheControl = "no-store";

        return TypedResults.Text(answer!, "application/xml", Encoding.UTF8);
    }

    private static async Task<Results<Ok<AgentJoinDomainCredentials>, ForbidHttpResult, UnauthorizedHttpResult, NotFound, ProblemHttpResult>> ReadJoinCredentialsAsync(
        Guid id,
        Guid runId,
        Guid stepId,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        RunSecrets secrets,
        CancellationToken cancellationToken)
    {
        if (await SecretMachineAsync(id, user, database, cancellationToken).ConfigureAwait(false) is not { } machine)
        {
            return Principals.IsMachine(user, id) ? TypedResults.Unauthorized() : TypedResults.Forbid(authenticationSchemes: [DdtAuthenticationSchemes.Machine]);
        }

        (AgentJoinDomainCredentials? credentials, string? refusal) = await secrets
            .JoinCredentialsAsync(machine, runId, stepId, context.Connection.RemoteIpAddress?.ToString(), cancellationToken)
            .ConfigureAwait(false);

        if (refusal is not null)
        {
            return TypedResults.Problem(title: refusal, statusCode: StatusCodes.Status409Conflict);
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Response.Headers.CacheControl = "no-store";

        return TypedResults.Ok(credentials!);
    }

    private static async Task<Results<Ok<AgentStepAccounts>, ForbidHttpResult, UnauthorizedHttpResult, NotFound, ProblemHttpResult>> ReadStepAccountsAsync(
        Guid id,
        Guid runId,
        Guid stepId,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        RunSecrets secrets,
        CancellationToken cancellationToken)
    {
        if (await SecretMachineAsync(id, user, database, cancellationToken).ConfigureAwait(false) is not { } machine)
        {
            return Principals.IsMachine(user, id) ? TypedResults.Unauthorized() : TypedResults.Forbid(authenticationSchemes: [DdtAuthenticationSchemes.Machine]);
        }

        (AgentStepAccounts? accounts, string? refusal) = await secrets
            .StepAccountsAsync(machine, runId, stepId, context.Connection.RemoteIpAddress?.ToString(), cancellationToken)
            .ConfigureAwait(false);

        if (refusal is not null)
        {
            return TypedResults.Problem(title: refusal, statusCode: StatusCodes.Status409Conflict);
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Response.Headers.CacheControl = "no-store";

        return TypedResults.Ok(accounts!);
    }

    // The machine the token names, in the token's generation, or null.
    private static async Task<Machine?> SecretMachineAsync(Guid id, ClaimsPrincipal user, DdtDbContext database, CancellationToken cancellationToken)
    {
        if (!Principals.IsMachine(user, id))
        {
            return null;
        }

        Machine? machine = await database.Machines.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);

        return machine is not null && Principals.HoldsCurrentGeneration(user, machine) ? machine : null;
    }

    // The run token comes with every answer while the run runs, so the agent has one on disk before it first restarts.
    private static AgentRunReportResult Tokens(Machine machine, Deployment run, MachineRegistrar registrar, MachineTokenService tokens) =>
        new(
            registrar.CurrentToken(machine),
            tokens.Issue(machine, MachineTokenPurpose.Resume),
            run.State == DeploymentState.Running ? tokens.IssueRunToken(machine, run.Id) : null);
}
