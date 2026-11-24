// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Records;
using DDT.Server.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

// What a run's Running step hands the server: a recovery key, a hardware hash. Only the machine's own session token,
// in its current generation.
public static class AgentRecordEndpoints
{
    public static RouteGroupBuilder MapAgentRecordEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/{id:guid}/runs/{runId:guid}/records", SaveAsync)
            .AddEndpointFilter<AgentMachineFilter>()
            .RequireAuthorization(DdtPolicies.Machine)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine)
            .WithMetadata(new RequestSizeLimitAttribute(RecordLimits.MaxRequestBytes));

        group.MapGet("/{id:guid}/runs/{runId:guid}/records/hardwareHash", ReadHardwareHashAsync)
            .AddEndpointFilter<AgentMachineFilter>()
            .RequireAuthorization(DdtPolicies.Machine)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine);

        return group;
    }

    private static async Task<Results<NoContent, ForbidHttpResult, UnauthorizedHttpResult, NotFound, ProblemHttpResult>> SaveAsync(
        [AsParameters] AgentRunRoute route,
        AgentRecord record,
        HttpContext context,
        RunRecords records,
        CancellationToken cancellationToken)
    {
        RecordRefusal? refusal = await records
            .SaveAsync(
                AgentMachineFilter.MachineOf(context),
                route.RunId,
                record,
                Actor.OfMachine(route.Id, context.Connection.RemoteIpAddress?.ToString()),
                cancellationToken)
            .ConfigureAwait(false);

        return refusal is null
            ? TypedResults.NoContent()
            : TypedResults.Problem(title: refusal.Title, statusCode: refusal.StatusCode);
    }

    private static async Task<Results<Ok<AgentHardwareHashStatus>, ForbidHttpResult, UnauthorizedHttpResult, NotFound, ProblemHttpResult>> ReadHardwareHashAsync(
        [AsParameters] AgentRunRoute route,
        HttpContext context,
        RunRecords records,
        CancellationToken cancellationToken)
    {
        (AgentHardwareHashStatus? status, RecordRefusal? refusal) = await records
            .HardwareHashStatusAsync(AgentMachineFilter.MachineOf(context), route.RunId, cancellationToken)
            .ConfigureAwait(false);

        return status is null || refusal is not null
            ? TypedResults.Problem(title: refusal?.Title, statusCode: refusal?.StatusCode ?? StatusCodes.Status409Conflict)
            : TypedResults.Ok(status);
    }
}
