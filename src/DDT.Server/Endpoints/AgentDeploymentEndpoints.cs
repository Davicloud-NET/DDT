// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using DDT.Contracts.Agents;
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

namespace DDT.Server.Endpoints;

// What an authorized machine needs to run a task sequence: the sequences it may choose and its run. All of it takes
// a session token, and the machine's own current generation.
public static class AgentDeploymentEndpoints
{
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
}
