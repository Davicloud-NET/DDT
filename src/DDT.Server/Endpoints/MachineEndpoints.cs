// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Messages;
using DDT.Contracts.Rules;
using DDT.Core.Machines;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using DDT.Server.Rules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Endpoints;

public static class MachineEndpoints
{
    public static RouteGroupBuilder MapMachineEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapGet("/models", ListModelsAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapGet("/{id:guid}/log", ReadLogAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapGet("/{id:guid}/sequence", ResolveSequenceAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapPost("/{id:guid}/approve", ApproveAsync).RequireAuthorization(DdtPolicies.Operator);
        group.MapPost("/{id:guid}/reject", RejectAsync).RequireAuthorization(DdtPolicies.Operator);
        group.MapGet("/{id:guid}/deployments", ListDeploymentsAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapPost("/{id:guid}/deployments", AssignAsync).RequireAuthorization(DdtPolicies.Operator);
        group.MapDelete("/{id:guid}/deployments/current", EndCurrentAsync).RequireAuthorization(DdtPolicies.Operator);

        // An operator answers what the run waits for at its start. Like the questions at the machine, it needs no new
        // sign-in: whoever may assign the run may answer it.
        group.MapPost("/{id:guid}/deployments/current/answers", AnswerAsync).RequireAuthorization(DdtPolicies.Operator);
        group.MapPost("/{id:guid}/deployments/current/continue", ContinueAsync).RequireAuthorization(DdtPolicies.Operator);

        // Anyone who reaches the server can register machines, so an operator can throw away the ones nobody
        // vouched for, one at a time or everything waiting from one address.
        group.MapDelete("/{id:guid}", RemoveAsync).RequireAuthorization(DdtPolicies.Operator);
        group.MapDelete("/", RemoveWaitingFromAsync).RequireAuthorization(DdtPolicies.Operator);

        return group;
    }

    // SQLite cannot order by DateTimeOffset, and a fleet this size sorts in memory for nothing. The order must not depend
    // on anything a poll changes, or rows move under an operator's pointer.
    private static async Task<Ok<IReadOnlyList<MachineSummary>>> ListAsync(
        DdtDbContext database,
        RunQueries runs,
        CancellationToken cancellationToken)
    {
        List<Machine> machines = await database.Machines.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<Guid, Deployment> shown = await runs.ShownForAsync(machines, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<MachineSummary>>(
        [
            .. machines
                .OrderBy(m => m.State == MachineState.Pending ? 0 : 1)
                .ThenByDescending(m => m.FirstSeenUtc)
                .ThenBy(m => m.Id)
                .Select(m => MachineSummaries.From(m, shown.GetValueOrDefault(m.Id))),
        ]);
    }

    // The models the registered machines report, for choosing a rule's or a package's model from what exists.
    // Placeholders are left out, because neither can match them.
    private static async Task<Ok<IReadOnlyList<HardwareModelCount>>> ListModelsAsync(DdtDbContext database, CancellationToken cancellationToken)
    {
        var reported = await database.Machines
            .AsNoTracking()
            .Select(m => new { m.Manufacturer, m.Model })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<HardwareModelCount>>(
        [
            .. reported
                .Select(m => (
                    Manufacturer: HardwareModels.IsPlaceholder(m.Manufacturer) ? null : HardwareModels.Clean(m.Manufacturer),
                    Model: HardwareModels.Clean(m.Model)))
                .Where(m => m.Model is not null && !HardwareModels.IsPlaceholder(m.Model))
                .GroupBy(m => (HardwareModels.Normalize(m.Manufacturer), HardwareModels.Normalize(m.Model)))
                .Select(group => (
                    Spelling: group.OrderBy(m => m.Manufacturer, StringComparer.Ordinal).ThenBy(m => m.Model, StringComparer.Ordinal).First(),
                    Count: group.Count()))
                .Select(model => new HardwareModelCount(model.Spelling.Manufacturer, model.Spelling.Model!, model.Count))
                .OrderByDescending(m => m.Machines)
                .ThenBy(m => m.Manufacturer, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.Model, StringComparer.OrdinalIgnoreCase),
        ]);
    }

    private static async Task<Results<Ok<MachineSequenceResolution>, NotFound>> ResolveSequenceAsync(
        Guid id,
        SequencePreviews previews,
        CancellationToken cancellationToken) =>
        await previews.PreviewAsync(id, cancellationToken).ConfigureAwait(false) is { } preview
            ? TypedResults.Ok(preview)
            : TypedResults.NotFound();

    private static async Task<Results<Ok<MachineLogPage>, NotFound>> ReadLogAsync(
        [AsParameters] MachineLogQuery query,
        MachineLogs logs,
        CancellationToken cancellationToken) =>
        await logs.ReadAsync(query, cancellationToken).ConfigureAwait(false) is { } page
            ? TypedResults.Ok(page)
            : TypedResults.NotFound();

    private static async Task<Results<Ok<MachineSummary>, NotFound, ProblemHttpResult, ValidationProblem>> ApproveAsync(
        Guid id,
        ApproveMachineRequest? request,
        HttpContext context,
        MachineApprovals approvals,
        CancellationToken cancellationToken) =>
        Answer(await approvals.ApproveAsync(id, request, Actor.Of(context), cancellationToken).ConfigureAwait(false));

    // Newest first. SQLite cannot order by DateTimeOffset, and one machine has few runs, so they are ordered here.
    private static async Task<Results<Ok<IReadOnlyList<DeploymentSummary>>, NotFound>> ListDeploymentsAsync(
        Guid id,
        DdtDbContext database,
        CancellationToken cancellationToken)
    {
        if (!await database.Machines.AnyAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false))
        {
            return TypedResults.NotFound();
        }

        List<Deployment> runs = await database.Deployments
            .AsNoTracking()
            .Where(d => d.MachineId == id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<DeploymentSummary>>(
        [
            .. runs.OrderByDescending(d => d.CreatedUtc).ThenByDescending(d => d.Id).Select(DeploymentSummaries.From),
        ]);
    }

    private static async Task<Results<Ok<MachineSummary>, NotFound, ProblemHttpResult, ValidationProblem>> RejectAsync(
        Guid id,
        HttpContext context,
        MachineTransitions transitions,
        CancellationToken cancellationToken) =>
        Answer(await transitions.RejectAsync(id, Actor.Of(context), cancellationToken).ConfigureAwait(false));

    private static async Task<Results<Ok<MachineSummary>, NotFound, ProblemHttpResult, ValidationProblem>> AssignAsync(
        Guid id,
        AssignSequenceRequest request,
        HttpContext context,
        MachineRuns runs,
        CancellationToken cancellationToken) =>
        Answer(await runs.AssignAsync(id, request, Actor.Of(context), cancellationToken).ConfigureAwait(false));

    private static async Task<Results<Ok<MachineSummary>, NotFound, ProblemHttpResult, ValidationProblem>> EndCurrentAsync(
        Guid id,
        HttpContext context,
        MachineRuns runs,
        CancellationToken cancellationToken) =>
        Answer(await runs.EndCurrentAsync(id, Actor.Of(context), cancellationToken).ConfigureAwait(false));

    // Answers the inputs the run waits for at its start. While it waits for nothing, or when the machine answered
    // first, the answer is the run as it is with 409.
    private static async Task<Results<Ok<DeploymentView>, Conflict<DeploymentView>, NotFound, ValidationProblem>> AnswerAsync(
        Guid id,
        AnswerInputsRequest request,
        HttpContext context,
        WaitingRunSaves saves,
        CancellationToken cancellationToken) =>
        Answer(await saves.AnswerAsync(id, request, Actor.Of(context), cancellationToken).ConfigureAwait(false));

    // Continues the pause the run waits at. When it no longer waits at that pause, the answer is the run as it is with 409.
    private static async Task<Results<Ok<DeploymentView>, Conflict<DeploymentView>, NotFound, ValidationProblem>> ContinueAsync(
        Guid id,
        ContinueRunRequest request,
        HttpContext context,
        WaitingRunSaves saves,
        CancellationToken cancellationToken) =>
        Answer(await saves.ContinueAsync(id, request, Actor.Of(context), cancellationToken).ConfigureAwait(false));

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> RemoveAsync(
        Guid id,
        HttpContext context,
        MachineRemovals removals,
        CancellationToken cancellationToken)
    {
        (bool found, ServerMessage? refusal) = await removals.RemoveAsync(id, Actor.Of(context), cancellationToken).ConfigureAwait(false);

        if (!found)
        {
            return TypedResults.NotFound();
        }

        return refusal is null ? TypedResults.NoContent() : ServerProblems.Problem(refusal, StatusCodes.Status409Conflict);
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> RemoveWaitingFromAsync(
        string waitingFrom,
        HttpContext context,
        MachineRemovals removals,
        CancellationToken cancellationToken) =>
        await removals.RemoveWaitingFromAsync(waitingFrom, Actor.Of(context), cancellationToken).ConfigureAwait(false) is { } refusal
            ? ServerProblems.Problem(refusal, StatusCodes.Status409Conflict)
            : TypedResults.NoContent();

    private static Results<Ok<MachineSummary>, NotFound, ProblemHttpResult, ValidationProblem> Answer(MachineOutcome outcome) => outcome switch
    {
        { Summary: { } summary } => TypedResults.Ok(summary),
        { Refusal: { Outcome: DeploymentOutcome.Invalid } invalid } => DeploymentDecisionResults.WebInvalid(invalid),
        { Refusal: { } refused } => DeploymentDecisionResults.WebProblem(
            refused,
            DeploymentDecisionResults.RefusalStatus(refused) ?? StatusCodes.Status409Conflict),
        _ => TypedResults.NotFound(),
    };

    private static Results<Ok<DeploymentView>, Conflict<DeploymentView>, NotFound, ValidationProblem> Answer(WaitingRunOutcome outcome) => outcome switch
    {
        { Invalid: { } invalid } => DeploymentDecisionResults.WebInvalid(invalid),
        { Changed: true } => TypedResults.Ok<DeploymentView>(outcome.View),
        { View: { } view } => TypedResults.Conflict(view),
        _ => TypedResults.NotFound(),
    };
}
