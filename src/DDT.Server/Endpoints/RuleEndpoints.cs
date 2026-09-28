// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Rules;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Rules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

// Rules choose code to run as SYSTEM, and the values it runs with, without anyone choosing each time, so only an
// administrator writes them. Every answer is what changed: the rule, or the whole list where places moved.
public static class RuleEndpoints
{
    public static RouteGroupBuilder MapRuleEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapPost("/", CreateAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/order", ReorderAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static async Task<Ok<IReadOnlyList<RuleView>>> ListAsync(DdtDbContext database, CancellationToken cancellationToken) =>
        TypedResults.Ok<IReadOnlyList<RuleView>>(await RuleViews.ListAsync(database, cancellationToken).ConfigureAwait(false));

    private static async Task<Results<Created<RuleView>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        SaveRuleRequest request,
        HttpContext context,
        RuleEditor editor,
        CancellationToken cancellationToken)
    {
        EditOutcome<RuleView> outcome = await editor.CreateAsync(request, Actor.Of(context), cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            { Saved: { } rule } => TypedResults.Created($"/api/rules/{rule.Id:D}", rule),
            { Refusal: { } refusal } => ServerProblems.Problem(refusal, StatusCodes.Status409Conflict),
            _ => (outcome.Problems ?? new FieldProblems()).ToResult(),
        };
    }

    // The client decides what to do with a newer save: take it, or save its own edits over it knowingly.
    private static async Task<Results<Ok<RuleView>, NotFound, Conflict<RuleView>, ValidationProblem>> UpdateAsync(
        Guid id,
        SaveRuleRequest request,
        HttpContext context,
        RuleEditor editor,
        CancellationToken cancellationToken)
    {
        EditOutcome<RuleView> outcome = await editor.UpdateAsync(id, request, Actor.Of(context), cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            { Saved: { } rule } => TypedResults.Ok(rule),
            { Current: { } current } => TypedResults.Conflict(current),
            { Problems: { } problems } => problems.ToResult(),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<Ok<IReadOnlyList<RuleView>>, NotFound>> DeleteAsync(
        Guid id,
        HttpContext context,
        RuleEditor editor,
        CancellationToken cancellationToken) =>
        await editor.DeleteAsync(id, Actor.Of(context), cancellationToken).ConfigureAwait(false) is { } rules
            ? TypedResults.Ok<IReadOnlyList<RuleView>>(rules)
            : TypedResults.NotFound();

    private static async Task<Results<Ok<IReadOnlyList<RuleView>>, Conflict<IReadOnlyList<RuleView>>, ValidationProblem>> ReorderAsync(
        ReorderRulesRequest request,
        HttpContext context,
        RuleEditor editor,
        CancellationToken cancellationToken)
    {
        EditOutcome<IReadOnlyList<RuleView>> outcome = await editor
            .ReorderAsync(request.RuleIds ?? [], Actor.Of(context), cancellationToken)
            .ConfigureAwait(false);

        return outcome switch
        {
            { Saved: { } rules } => TypedResults.Ok(rules),
            { Current: { } current } => TypedResults.Conflict(current),
            _ => (outcome.Problems ?? new FieldProblems()).ToResult(),
        };
    }
}
