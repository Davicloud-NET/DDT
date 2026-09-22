// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;

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
            deployment.Error);
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
            step.Error);
    }

    public static DeploymentArtifactView Artifact(DeploymentArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        return new DeploymentArtifactView(artifact.StepId, artifact.Kind, artifact.SourceId, artifact.Name, artifact.Sha256, artifact.SizeBytes);
    }
}
