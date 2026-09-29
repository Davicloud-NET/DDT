// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;

namespace DDT.Server.Deployments;

public static class DeploymentSummaries
{
    public static DeploymentSummary From(Deployment deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return new DeploymentSummary(
            deployment.Id,
            deployment.TaskSequenceId,
            deployment.Title,
            deployment.State,
            deployment.Source,
            deployment.RequestedByName,
            deployment.StepCount,
            deployment.CurrentStepIndex,
            deployment.CurrentStepName,
            deployment.Percent,
            deployment.CurrentPhase,
            deployment.Activity,
            deployment.CreatedUtc,
            deployment.StartedUtc,
            deployment.FinishedUtc,
            deployment.UpdatedUtc,
            deployment.Error,
            Waiting(deployment),
            deployment is { State: DeploymentState.Running, PauseStepId: not null } && !Continued(deployment) ? deployment.PauseMessage : null);
    }

    // The run needs someone: answers to its inputs before it can start, or someone to continue the pause it waits at.
    public static bool Waiting(Deployment deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment switch
        {
            { State: DeploymentState.Assigned, InputsPending: true } => true,
            { State: DeploymentState.Running, PauseStepId: not null } => !Continued(deployment),
            _ => false,
        };
    }

    // Someone continued the pause the run waits at. Its agent continues once its next report learns that.
    public static bool Continued(Deployment deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.PauseStepId is { } stepId && deployment.ContinueStepId == stepId && deployment.ContinuePass == deployment.PausePass;
    }

    public static DeploymentStepView Step(DeploymentStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        return new DeploymentStepView(
            step.StepId,
            step.Index,
            step.Name,
            step.Kind,
            step.Phase,
            step.State,
            step.Percent,
            step.StartedUtc,
            step.FinishedUtc,
            step.Error,
            step.ParentId,
            step.Depth,
            step.Pass,
            step.Iteration,
            step.Branch,
            ReadEvaluation(step.Evaluation));
    }

    public static string? WriteEvaluation(IReadOnlyList<TestEvaluation>? evaluation) =>
        evaluation is null ? null : JsonSerializer.Serialize(evaluation, DdtJsonContext.Default.IReadOnlyListTestEvaluation);

    // Null for none, and for one written by another build that no longer reads. It only explains a decision.
    public static IReadOnlyList<TestEvaluation>? ReadEvaluation(string? evaluation)
    {
        if (evaluation is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(evaluation, DdtJsonContext.Default.IReadOnlyListTestEvaluation);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static DeploymentArtifactView Artifact(DeploymentArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        return new DeploymentArtifactView(artifact.StepId, artifact.Kind, artifact.SourceId, artifact.Name, artifact.Sha256, artifact.SizeBytes);
    }
}
