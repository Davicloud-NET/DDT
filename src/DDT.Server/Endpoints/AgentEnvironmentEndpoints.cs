// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Server.Authentication;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using DDT.Server.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

// What a machine tells the server before it restarts into the other boot environment, with its own session token.
public static class AgentEnvironmentEndpoints
{
    public static RouteGroupBuilder MapAgentEnvironmentEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/{id:guid}/environment", SwitchAsync)
            .AddEndpointFilter<AgentMachineFilter>()
            .RequireAuthorization(DdtPolicies.Machine)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine)
            .WithMetadata(new RequestSizeLimitAttribute(DeploymentLimits.MaxRequestBytes));

        return group;
    }

    // 403 while nobody who may pick is signed in at the machine, and 409 with the reason for any other refusal.
    private static async Task<Results<NoContent, ForbidHttpResult, UnauthorizedHttpResult, NotFound, ProblemHttpResult>> SwitchAsync(
        AgentEnvironmentRequest request,
        HttpContext context,
        EnvironmentSwitches switches,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        EnvironmentSwitchOutcome outcome = await switches
            .RequestAsync(
                AgentMachineFilter.MachineOf(context),
                request,
                context.Connection.RemoteIpAddress?.ToString(),
                timeProvider.GetUtcNow(),
                cancellationToken)
            .ConfigureAwait(false);

        return outcome.Refusal is null
            ? TypedResults.NoContent()
            : TypedResults.Problem(
                title: outcome.Refusal,
                statusCode: outcome.MayNotPick ? StatusCodes.Status403Forbidden : StatusCodes.Status409Conflict);
    }
}
