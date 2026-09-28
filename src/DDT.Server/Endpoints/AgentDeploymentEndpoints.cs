// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text;
using DDT.Contracts.Agents;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using DDT.Server.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace DDT.Server.Endpoints;

// What an authorized machine needs to run a task sequence: the sequences it may choose, its run, the run's files and,
// just in time, its secrets. Everything needs the machine's own session token, in its current generation.
public static class AgentDeploymentEndpoints
{
    public static RouteGroupBuilder MapAgentDeploymentEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/{id:guid}/sequences", ListSequencesAsync)
            .AddEndpointFilter<AgentMachineFilter>()
            .RequireAuthorization(DdtPolicies.Machine)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine);

        group.MapPost("/{id:guid}/runs", PickAsync)
            .AddEndpointFilter<AgentMachineFilter>()
            .RequireAuthorization(DdtPolicies.Machine)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine)
            .WithMetadata(new RequestSizeLimitAttribute(DeploymentLimits.MaxRequestBytes));

        group.MapPost("/{id:guid}/runs/{runId:guid}/report", ReportAsync)
            .RequireAuthorization(DdtPolicies.Machine)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine)
            .WithMetadata(new RequestSizeLimitAttribute(DeploymentLimits.MaxReportBytes));

        group.MapPost("/{id:guid}/runs/{runId:guid}/answers", AnswerAsync)
            .AddEndpointFilter<AgentMachineFilter>()
            .RequireAuthorization(DdtPolicies.Machine)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine)
            .WithMetadata(new RequestSizeLimitAttribute(DeploymentLimits.MaxRequestBytes));

        // HEAD is mapped explicitly. The agent checks a file's size before it erases the disk, and an unmatched HEAD
        // would fall through to the web UI's index page with 200.
        group.MapMethods("/{id:guid}/runs/{runId:guid}/files/{sha256}", [HttpMethods.Get, HttpMethods.Head], ReadFileAsync)
            .AddEndpointFilter<AgentMachineFilter>()
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
        // call these. They're answered here, not by the web UI's fallback page, which would answer a GET with 200.
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
        HttpContext context,
        SequenceChoices choices,
        CancellationToken cancellationToken)
    {
        Machine machine = AgentMachineFilter.MachineOf(context);

        if (!await choices.CanPickAsync(machine, cancellationToken).ConfigureAwait(false))
        {
            return TypedResults.Problem(
                title: "Only an operator or administrator signed in at this machine can choose a sequence. Sign in at the machine first.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        return TypedResults.Ok(await choices.ChoicesAsync(machine, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<Results<Ok<AgentRun>, ForbidHttpResult, UnauthorizedHttpResult, NotFound, ProblemHttpResult, ValidationProblem>> PickAsync(
        AgentRunRequest request,
        HttpContext context,
        SequencePickSaves picks,
        CancellationToken cancellationToken)
    {
        (AgentRun? run, DeploymentDecision decision) = await picks
            .PickAsync(AgentMachineFilter.MachineOf(context), request, context.Connection.RemoteIpAddress?.ToString(), cancellationToken)
            .ConfigureAwait(false);

        if (run is not null)
        {
            return TypedResults.Ok(run);
        }

        return decision.Outcome == DeploymentOutcome.Invalid
            ? DeploymentDecisionResults.AgentInvalid(decision)
            : DeploymentDecisionResults.AgentProblem(decision, DeploymentDecisionResults.RefusalStatus(decision) ?? StatusCodes.Status409Conflict);
    }

    private static async Task<Results<Ok<AgentRunReportResult>, ForbidHttpResult, UnauthorizedHttpResult, NotFound, ProblemHttpResult, ValidationProblem>> ReportAsync(
        [AsParameters] AgentRunRoute route,
        AgentRunReport report,
        HttpContext context,
        RunReportSaves reports,
        CancellationToken cancellationToken)
    {
        if (!Principals.IsMachine(context.User, route.Id))
        {
            return TypedResults.Forbid(authenticationSchemes: [DdtAuthenticationSchemes.Machine]);
        }

        IncomingReport incoming = new(route.Id, route.RunId, report, context.User, context.Connection.RemoteIpAddress?.ToString());
        ReportOutcome outcome = await reports.SaveAsync(incoming, cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            { Result: { } result } => TypedResults.Ok(result),
            { Refusal: { Outcome: DeploymentOutcome.Invalid } invalid } => DeploymentDecisionResults.AgentInvalid(invalid),
            { Refusal: { } refused } => DeploymentDecisionResults.AgentProblem(
                refused,
                DeploymentDecisionResults.RefusalStatus(refused) ?? StatusCodes.Status409Conflict),
            { Unauthorized: true } => TypedResults.Unauthorized(),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<Ok<AgentAnswersResult>, ForbidHttpResult, UnauthorizedHttpResult, NotFound, ProblemHttpResult>> AnswerAsync(
        Guid runId,
        AgentInputAnswers answers,
        HttpContext context,
        AgentAnswerSaves saves,
        CancellationToken cancellationToken)
    {
        AgentAnswering answering = await saves
            .AnswerAsync(AgentMachineFilter.MachineOf(context), runId, answers.Answers, context.Connection.RemoteIpAddress?.ToString(), cancellationToken)
            .ConfigureAwait(false);

        return answering.Result is { } result
            ? TypedResults.Ok(result)
            : TypedResults.Problem(
                title: answering.Refusal,
                statusCode: answering.NoSuchRun ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict);
    }

    // Content addressed: the tag is the hash, so a resumed range can never splice two different files.
    private static async Task<Results<PhysicalFileHttpResult, ForbidHttpResult, UnauthorizedHttpResult, NotFound, ProblemHttpResult>> ReadFileAsync(
        Guid runId,
        string sha256,
        HttpContext context,
        RunFiles files,
        CancellationToken cancellationToken)
    {
        (DeploymentArtifact? artifact, string? path) = await files
            .FindAsync(AgentMachineFilter.MachineOf(context), runId, sha256, cancellationToken)
            .ConfigureAwait(false);

        if (artifact is null)
        {
            return TypedResults.Problem(
                title: "This machine's run has no such file. Ask the server for the current run.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (path is null)
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
        [AsParameters] AgentStepRoute route,
        HttpContext context,
        DdtDbContext database,
        RunSecrets secrets,
        CancellationToken cancellationToken)
    {
        if (await SecretMachineAsync(route.Id, context, database, cancellationToken).ConfigureAwait(false) is not { } machine)
        {
            return Principals.IsMachine(context.User, route.Id)
                ? TypedResults.Unauthorized()
                : TypedResults.Forbid(authenticationSchemes: [DdtAuthenticationSchemes.Machine]);
        }

        (string? answer, string? refusal) = await secrets
            .AnswerFileAsync(machine, route.RunId, route.StepId, context.Connection.RemoteIpAddress?.ToString(), cancellationToken)
            .ConfigureAwait(false);

        if (refusal is not null || answer is null)
        {
            return TypedResults.Problem(title: refusal, statusCode: StatusCodes.Status409Conflict);
        }

        await SavedAsync(context, database, cancellationToken).ConfigureAwait(false);

        return TypedResults.Text(answer, "application/xml", Encoding.UTF8);
    }

    private static async Task<Results<Ok<AgentJoinDomainCredentials>, ForbidHttpResult, UnauthorizedHttpResult, NotFound, ProblemHttpResult>> ReadJoinCredentialsAsync(
        [AsParameters] AgentStepRoute route,
        HttpContext context,
        DdtDbContext database,
        RunSecrets secrets,
        CancellationToken cancellationToken)
    {
        if (await SecretMachineAsync(route.Id, context, database, cancellationToken).ConfigureAwait(false) is not { } machine)
        {
            return Principals.IsMachine(context.User, route.Id)
                ? TypedResults.Unauthorized()
                : TypedResults.Forbid(authenticationSchemes: [DdtAuthenticationSchemes.Machine]);
        }

        (AgentJoinDomainCredentials? credentials, string? refusal) = await secrets
            .JoinCredentialsAsync(machine, route.RunId, route.StepId, context.Connection.RemoteIpAddress?.ToString(), cancellationToken)
            .ConfigureAwait(false);

        if (refusal is not null || credentials is null)
        {
            return TypedResults.Problem(title: refusal, statusCode: StatusCodes.Status409Conflict);
        }

        await SavedAsync(context, database, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(credentials);
    }

    private static async Task<Results<Ok<AgentStepAccounts>, ForbidHttpResult, UnauthorizedHttpResult, NotFound, ProblemHttpResult>> ReadStepAccountsAsync(
        [AsParameters] AgentStepRoute route,
        HttpContext context,
        DdtDbContext database,
        StepAccounts accounts,
        CancellationToken cancellationToken)
    {
        if (await SecretMachineAsync(route.Id, context, database, cancellationToken).ConfigureAwait(false) is not { } machine)
        {
            return Principals.IsMachine(context.User, route.Id)
                ? TypedResults.Unauthorized()
                : TypedResults.Forbid(authenticationSchemes: [DdtAuthenticationSchemes.Machine]);
        }

        (AgentStepAccounts? read, string? refusal) = await accounts
            .ReadAsync(machine, route.RunId, route.StepId, context.Connection.RemoteIpAddress?.ToString(), cancellationToken)
            .ConfigureAwait(false);

        if (refusal is not null || read is null)
        {
            return TypedResults.Problem(title: refusal, statusCode: StatusCodes.Status409Conflict);
        }

        await SavedAsync(context, database, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(read);
    }

    // The machine the token names, in the token's generation, or null.
    private static async Task<Machine?> SecretMachineAsync(Guid id, HttpContext context, DdtDbContext database, CancellationToken cancellationToken)
    {
        if (!Principals.IsMachine(context.User, id))
        {
            return null;
        }

        Machine? machine = await database.Machines.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);

        return machine is not null && Principals.HoldsCurrentGeneration(context.User, machine) ? machine : null;
    }

    // The read's audit rows are saved before the secret leaves, and no-store tells anything in between not to keep it.
    private static async Task SavedAsync(HttpContext context, DdtDbContext database, CancellationToken cancellationToken)
    {
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Response.Headers.CacheControl = "no-store";
    }
}
