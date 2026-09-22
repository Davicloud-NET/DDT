// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Deployments;

internal static partial class DeploymentLog
{
    [LoggerMessage(EventId = 950, Level = LogLevel.Information, Message = "Deployment {DeploymentId} of {Title} assigned to machine {MachineId} by {RequestedBy}")]
    public static partial void Assigned(ILogger logger, Guid deploymentId, string title, Guid machineId, string requestedBy);

    [LoggerMessage(EventId = 951, Level = LogLevel.Information, Message = "Deployment {DeploymentId} of {Title} started on machine {MachineId}")]
    public static partial void Started(ILogger logger, Guid deploymentId, string title, Guid machineId);

    [LoggerMessage(EventId = 952, Level = LogLevel.Information, Message = "Deployment {DeploymentId} of {Title} finished on machine {MachineId}")]
    public static partial void Done(ILogger logger, Guid deploymentId, string title, Guid machineId);

    [LoggerMessage(EventId = 953, Level = LogLevel.Warning, Message = "Deployment {DeploymentId} of {Title} failed on machine {MachineId} at step {StepName}: {Error}")]
    public static partial void Failed(ILogger logger, Guid deploymentId, string title, Guid machineId, string? stepName, string? error);

    [LoggerMessage(EventId = 954, Level = LogLevel.Information, Message = "Deployment {DeploymentId} of {Title} on machine {MachineId} cancelled")]
    public static partial void Cancelled(ILogger logger, Guid deploymentId, string title, Guid machineId);

    [LoggerMessage(EventId = 955, Level = LogLevel.Warning, Message = "Deployment {DeploymentId} of {Title} failed on machine {MachineId} before it started: {Error}")]
    public static partial void FailedBeforeStart(ILogger logger, Guid deploymentId, string title, Guid machineId, string? error);

    // Called after a successful save, so the log never claims a change that lost a race. Before is null for a new
    // deployment.
    public static void Changed(ILogger logger, Deployment deployment, DeploymentState? before)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        if (deployment.State == before)
        {
            return;
        }

        switch (deployment.State)
        {
            case DeploymentState.Assigned:
                Assigned(logger, deployment.Id, deployment.Title, deployment.MachineId, deployment.RequestedByName ?? "unknown");
                break;
            case DeploymentState.Running:
                Started(logger, deployment.Id, deployment.Title, deployment.MachineId);
                break;
            case DeploymentState.Done:
                Done(logger, deployment.Id, deployment.Title, deployment.MachineId);
                break;
            case DeploymentState.Failed when deployment.StartedUtc is null:
                FailedBeforeStart(logger, deployment.Id, deployment.Title, deployment.MachineId, deployment.Error);
                break;
            case DeploymentState.Failed:
                Failed(logger, deployment.Id, deployment.Title, deployment.MachineId, deployment.CurrentStepName, deployment.Error);
                break;
            case DeploymentState.Cancelled:
                Cancelled(logger, deployment.Id, deployment.Title, deployment.MachineId);
                break;
        }
    }
}
