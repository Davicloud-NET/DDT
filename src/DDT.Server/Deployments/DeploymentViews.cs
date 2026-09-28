// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Data;
using DDT.Server.Sequences;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// A run as its page shows it: the definition it was given, its rows, its files, the values it started with, the variables
// steps set, its inputs and whether each has its answer, and the pause it waits at. Never an answer to an Account input,
// nor who typed which password: only that an input has its answer and who gave it.
public static class DeploymentViews
{
    public static async Task<DeploymentView?> ReadAsync(DdtDbContext database, Guid runId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        Deployment? run = await database.Deployments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == runId, cancellationToken).ConfigureAwait(false);

        if (run is null)
        {
            return null;
        }

        DeploymentSnapshot? snapshot = await database.DeploymentSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.DeploymentId == runId, cancellationToken)
            .ConfigureAwait(false);

        List<DeploymentStep> steps = await database.DeploymentSteps
            .AsNoTracking()
            .Where(s => s.DeploymentId == runId)
            .OrderBy(s => s.Index)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<DeploymentArtifact> artifacts = await database.DeploymentArtifacts
            .AsNoTracking()
            .Where(a => a.DeploymentId == runId)
            .OrderBy(a => a.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        SequenceDefinition? definition = snapshot is null ? null : SequenceDocuments.Read(snapshot.Definition);
        IReadOnlyList<RunInputView>? inputs = null;

        if (definition?.Inputs is { Count: > 0 } declared)
        {
            // Who gave an account for the run, never the account: the answer itself stays for the step that uses it.
            var accounts = await database.RunCredentials
                .AsNoTracking()
                .Where(c => c.DeploymentId == runId)
                .Select(c => new { c.InputName, c.ProvidedByName })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            IReadOnlyList<RunAnswer> answers = RunAnswer.Read(run.Answers);

            inputs =
            [
                .. declared.OfType<InputDeclaration>().Select(input =>
                {
                    bool account = input.Kind == InputKind.Account;
                    RunAnswer? answer = account ? null : answers.FirstOrDefault(a => string.Equals(a.Name, input.Name, StringComparison.OrdinalIgnoreCase));
                    var credential = account ? accounts.FirstOrDefault(c => string.Equals(c.InputName, input.Name, StringComparison.OrdinalIgnoreCase)) : null;

                    return new RunInputView(
                        RunValues.Asked(input, null),
                        answer is not null || credential is not null,
                        answer?.AnsweredBy ?? credential?.ProvidedByName);
                }),
            ];
        }

        return new DeploymentView(
            DeploymentSummaries.From(run),
            run.MachineId,
            run.SequenceRevision,
            run.RuleId,
            definition,
            [.. steps.Select(DeploymentSummaries.Step)],
            [.. artifacts.Select(DeploymentSummaries.Artifact)],
            run.AllowSecureBootMismatch,
            RunValues.Read(run.Values),
            RunVariables.Read(run.Variables),
            inputs,
            Pause(run, definition, steps));
    }

    // The pause the run waits at, until someone continued it; the agent learns that with its next report.
    private static RunPauseView? Pause(Deployment run, SequenceDefinition? definition, List<DeploymentStep> steps)
    {
        if (run is not { State: DeploymentState.Running, PauseStepId: { } stepId, PausePass: { } pass } || DeploymentSummaries.Continued(run))
        {
            return null;
        }

        DateTimeOffset? since = steps.FirstOrDefault(step => step.StepId == stepId)?.StartedUtc;
        int? minutes = definition is null ? null : (SequenceTree.Index(definition).GetValueOrDefault(stepId)?.Step as PauseStep)?.ContinueAfterMinutes;

        return new RunPauseView(stepId, pass, run.PauseMessage, since, since is { } started && minutes is { } after ? started.AddMinutes(after) : null);
    }
}
