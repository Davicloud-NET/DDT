// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Rules;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Rules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

// Machine roles are sets of values that rules give machines. So, like rules, only an administrator writes them.
public static class MachineRoleEndpoints
{
    public static RouteGroupBuilder MapMachineRoleEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapPost("/", CreateAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static async Task<Ok<IReadOnlyList<MachineRoleView>>> ListAsync(DdtDbContext database, CancellationToken cancellationToken) =>
        TypedResults.Ok<IReadOnlyList<MachineRoleView>>(await RuleViews.ListRolesAsync(database, cancellationToken).ConfigureAwait(false));

    private static async Task<Results<Created<MachineRoleView>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        SaveMachineRoleRequest request,
        HttpContext context,
        MachineRoleEditor editor,
        CancellationToken cancellationToken)
    {
        EditOutcome<MachineRoleView> outcome = await editor.CreateAsync(request, Actor.Of(context), cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            { Saved: { } role } => TypedResults.Created($"/api/machine-roles/{role.Id:D}", role),
            { Refusal: { } refusal } => ServerProblems.Problem(refusal, StatusCodes.Status409Conflict),
            _ => (outcome.Problems ?? new FieldProblems()).ToResult(),
        };
    }

    // The client decides what to do with a newer save: take it, or knowingly save its own edits over it.
    private static async Task<Results<Ok<MachineRoleView>, NotFound, Conflict<MachineRoleView>, ValidationProblem>> UpdateAsync(
        Guid id,
        SaveMachineRoleRequest request,
        HttpContext context,
        MachineRoleEditor editor,
        CancellationToken cancellationToken)
    {
        EditOutcome<MachineRoleView> outcome = await editor.UpdateAsync(id, request, Actor.Of(context), cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            { Saved: { } role } => TypedResults.Ok(role),
            { Current: { } current } => TypedResults.Conflict(current),
            { Problems: { } problems } => problems.ToResult(),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(
        Guid id,
        HttpContext context,
        MachineRoleEditor editor,
        CancellationToken cancellationToken)
    {
        (bool found, ServerMessage? refusal) = await editor.DeleteAsync(id, Actor.Of(context), cancellationToken).ConfigureAwait(false);

        if (!found)
        {
            return TypedResults.NotFound();
        }

        return refusal is null ? TypedResults.NoContent() : ServerProblems.Problem(refusal, StatusCodes.Status409Conflict);
    }
}
