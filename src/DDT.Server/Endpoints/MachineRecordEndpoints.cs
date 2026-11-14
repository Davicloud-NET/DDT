// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;
using DDT.Contracts.Messages;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Records;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Endpoints;

// A machine's recovery keys and hardware hash. Everyone sees that keys exist. Only administrators reveal or delete one.
public static class MachineRecordEndpoints
{
    public static RouteGroupBuilder MapMachineRecordEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/{id:guid}/records", ReadAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapGet("/recovery-keys", SearchAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapPost("/{id:guid}/recovery-keys/{keyId:guid}/reveal", RevealAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapDelete("/{id:guid}/recovery-keys/{keyId:guid}", DeleteAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static async Task<Results<Ok<MachineRecordsView>, NotFound>> ReadAsync(
        Guid id,
        DdtDbContext database,
        MachineRecordViews views,
        CancellationToken cancellationToken)
    {
        if (!await database.Machines.AnyAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await views.ReadAsync(id, cancellationToken).ConfigureAwait(false));
    }

    // The machines with a key whose ID is or starts with keyId. Too short a start finds none.
    private static async Task<Ok<IReadOnlyList<Guid>>> SearchAsync(string? keyId, RecoveryKeySearch search, CancellationToken cancellationToken) =>
        TypedResults.Ok(await search.MachinesAsync(keyId, cancellationToken).ConfigureAwait(false));

    // no-store keeps the password out of every cache on the way.
    private static async Task<Results<Ok<RecoveryKeyReveal>, NotFound, ProblemHttpResult>> RevealAsync(
        Guid id,
        Guid keyId,
        HttpContext context,
        RecoveryKeyActions actions,
        CancellationToken cancellationToken)
    {
        (string? password, ServerMessage? refusal) = await actions.RevealAsync(id, keyId, Actor.Of(context), cancellationToken).ConfigureAwait(false);

        if (refusal is not null)
        {
            return ServerProblems.Problem(refusal, StatusCodes.Status409Conflict);
        }

        if (password is null)
        {
            return TypedResults.NotFound();
        }

        context.Response.Headers.CacheControl = "no-store";

        return TypedResults.Ok(new RecoveryKeyReveal(password));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(
        Guid id,
        Guid keyId,
        HttpContext context,
        RecoveryKeyActions actions,
        CancellationToken cancellationToken)
    {
        (bool found, ServerMessage? refusal) = await actions.DeleteAsync(id, keyId, Actor.Of(context), cancellationToken).ConfigureAwait(false);

        if (!found)
        {
            return TypedResults.NotFound();
        }

        return refusal is null ? TypedResults.NoContent() : ServerProblems.Problem(refusal, StatusCodes.Status409Conflict);
    }
}
