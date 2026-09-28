// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// What a run waits for someone to give: answers to its inputs before it starts, or a continue at a pause. Nothing here
// saves.
public sealed class WaitingRuns(
    DdtDbContext database,
    RunQueries queries,
    RunValues values,
    DdtSettings settings,
    TimeProvider timeProvider)
{
    // Takes answers while an assigned run lacks a required one. The machine only answers what it asks, and the agent
    // starts the run once none is missing. The caller saves over Before, so answers given elsewhere in the meantime
    // win.
    public async Task<RunAnswering> AnswerAsync(Machine machine, Deployment run, AnswersGiven given, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(given);

        SequenceDefinition definition = await queries.DefinitionAsync(run, cancellationToken).ConfigureAwait(false);
        DeploymentOptions deployment = settings.Current.Deployment;

        if (run.State != DeploymentState.Assigned)
        {
            return new RunAnswering(run.Answers, [], [], null, Refused: true);
        }

        RunValueCheck waiting = await values.CheckAsync(machine, run, definition, deployment, cancellationToken).ConfigureAwait(false);

        if (waiting.Missing.Count == 0)
        {
            return new RunAnswering(run.Answers, [], [], null, Refused: true);
        }

        List<AnswerProblem> problems =
            [.. RunValues.Check(definition, given.Answers, input => !given.AtMachine || input.AskAt is InputAsk.Machine or InputAsk.Both)];

        if (problems.Count > 0)
        {
            return new RunAnswering(run.Answers, waiting.AskedAtMachine, problems, null, Refused: false);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        string? before = run.Answers;

        if (await MergeAsync(run, definition, given, now, cancellationToken).ConfigureAwait(false) is { } account)
        {
            return new RunAnswering(before, waiting.AskedAtMachine, [account], null, Refused: false);
        }

        RunValueCheck check = await values.CheckAsync(machine, run, definition, deployment, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> answered = RunValues.Answered(definition, given.Answers);
        problems.AddRange(RunValues.AnsweredValueProblems(check, answered));

        if (problems.Count > 0)
        {
            return new RunAnswering(before, waiting.AskedAtMachine, problems, null, Refused: false);
        }

        run.InputsPending = check.Missing.Count > 0 && (given.AtMachine || run.InputsPending);
        run.UpdatedUtc = now;
        Actor answeredBy = given.Actor with { MachineId = given.AtMachine ? machine.Id : null };

        if (RunAnswerAudits.Of(run, answered, given.AtMachine, now, answeredBy) is { } audit)
        {
            database.AuditEvents.Add(audit);
        }

        return new RunAnswering(before, waiting.AskedAtMachine, [], check, Refused: false);
    }

    // Continues the pause the run waits at, in the visit the page showed, so a late click doesn't continue a later
    // visit. The agent learns it from the answer to its next report, which comes within seconds while it waits.
    public async Task<DeploymentDecision> ContinueAsync(
        Machine machine,
        ContinueRunRequest request,
        Actor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        Deployment? run = await queries.ActiveAsync(machine, cancellationToken).ConfigureAwait(false);

        if (run is not { State: DeploymentState.Running, PauseStepId: { } stepId, PausePass: { } pass }
            || stepId != request.StepId
            || pass != request.Pass
            || DeploymentSummaries.Continued(run))
        {
            return DeploymentDecision.Conflict("The run no longer waits at that pause.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        string? step = await database.DeploymentSteps
            .AsNoTracking()
            .Where(s => s.DeploymentId == run.Id && s.StepId == stepId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        run.ContinueStepId = stepId;
        run.ContinuePass = pass;
        run.ContinuedByName = StoredText.Bound(actor.Name, 256);
        run.UpdatedUtc = now;
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.DeploymentContinued,
            run.Id.ToString("D"),
            actor,
            now,
            $"{run.Title} on machine {machine.Id:D}, at {step ?? "its pause"} ({stepId:D}), visit {pass}."));

        return DeploymentDecision.Accepted(run);
    }

    // Adds the answers to the run's, and keeps the Account answers. The first account that can't be kept refuses them
    // all.
    private async Task<AnswerProblem?> MergeAsync(
        Deployment run,
        SequenceDefinition definition,
        AnswersGiven given,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RunAnswer> merged = RunValues.Merge(
            definition,
            RunAnswer.Read(run.Answers),
            given.Answers,
            new RunAnswerGiver(given.Actor.Name, given.AtMachine, now));
        run.Answers = merged.Count == 0 ? null : RunAnswer.Write(merged);

        return await values
            .KeepAccountsAsync(run, definition, given.Answers, new RunCredentialGiver(given.Actor.UserId, given.Actor.Name, given.AtMachine), cancellationToken)
            .ConfigureAwait(false);
    }
}
