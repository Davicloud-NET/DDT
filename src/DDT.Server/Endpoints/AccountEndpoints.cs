// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Accounts;
using DDT.Contracts.Messages;
using DDT.Server.Accounts;
using DDT.Server.Authentication;
using DDT.Server.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Endpoints;

// The accounts steps use, Deployment > Accounts. Everyone signed in reads them, never a password. Only an administrator
// changes them, with the password entered again, since an account reaches machines with whatever it may do in the domain.
public static class AccountEndpoints
{
    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapGet("/{id:guid}", ReadAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapPost("/", CreateAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPut("/{id:guid}", SaveAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static async Task<Ok<IReadOnlyList<AccountView>>> ListAsync(AccountViews views, CancellationToken cancellationToken) =>
        TypedResults.Ok(await views.ListAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<Results<Ok<AccountView>, NotFound>> ReadAsync(
        Guid id,
        DdtDbContext database,
        AccountViews views,
        CancellationToken cancellationToken)
    {
        Account? account = await database.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken).ConfigureAwait(false);

        return account is null ? TypedResults.NotFound() : TypedResults.Ok(await views.ViewAsync(account, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<Results<Created<AccountView>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        SaveAccountRequest request,
        [AsParameters] AccountWriteAccess access,
        AccountEditor editor,
        CancellationToken cancellationToken)
    {
        if (await access.RefusedAsync().ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        EditOutcome<AccountView> outcome = await editor.CreateAsync(request, access.Actor, cancellationToken).ConfigureAwait(false);

        return outcome.Saved is { } account
            ? TypedResults.Created($"/api/accounts/{account.Id:D}", account)
            : (outcome.Problems ?? new FieldProblems()).ToResult();
    }

    // The page decides what to do with a newer save: take it, or save its own over it knowingly.
    private static async Task<Results<Ok<AccountView>, Conflict<AccountView>, NotFound, ValidationProblem, ProblemHttpResult>> SaveAsync(
        Guid id,
        SaveAccountRequest request,
        [AsParameters] AccountWriteAccess access,
        AccountEditor editor,
        CancellationToken cancellationToken)
    {
        if (await access.RefusedAsync().ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        EditOutcome<AccountView> outcome = await editor.SaveAsync(id, request, access.Actor, cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            { Saved: { } account } => TypedResults.Ok(account),
            { Current: { } current } => TypedResults.Conflict(current),
            { Problems: { } problems } => problems.ToResult(),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(
        Guid id,
        [AsParameters] AccountWriteAccess access,
        AccountEditor editor,
        CancellationToken cancellationToken)
    {
        if (await access.RefusedAsync().ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        (bool found, ServerMessage? refusal) = await editor.DeleteAsync(id, access.Actor, cancellationToken).ConfigureAwait(false);

        if (!found)
        {
            return TypedResults.NotFound();
        }

        return refusal is null ? TypedResults.NoContent() : ServerProblems.Problem(refusal, StatusCodes.Status409Conflict);
    }
}
