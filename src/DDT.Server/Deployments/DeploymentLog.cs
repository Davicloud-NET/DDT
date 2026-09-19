using DDT.Contracts.Deployments;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Deployments;

internal static partial class DeploymentLog
{
    [LoggerMessage(EventId = 950, Level = LogLevel.Information, Message = "Deployment {DeploymentId} of {ImageName} assigned to machine {MachineId} by {RequestedBy}")]
    public static partial void Assigned(ILogger logger, Guid deploymentId, string imageName, Guid machineId, string requestedBy);

    [LoggerMessage(EventId = 951, Level = LogLevel.Information, Message = "Deployment {DeploymentId} of {ImageName} started on machine {MachineId}")]
    public static partial void Started(ILogger logger, Guid deploymentId, string imageName, Guid machineId);

    [LoggerMessage(EventId = 952, Level = LogLevel.Information, Message = "Deployment {DeploymentId} of {ImageName} finished on machine {MachineId}")]
    public static partial void Done(ILogger logger, Guid deploymentId, string imageName, Guid machineId);

    [LoggerMessage(EventId = 953, Level = LogLevel.Warning, Message = "Deployment {DeploymentId} of {ImageName} failed on machine {MachineId} at {Step}: {Error}")]
    public static partial void Failed(ILogger logger, Guid deploymentId, string imageName, Guid machineId, DeploymentStep? step, string? error);

    [LoggerMessage(EventId = 954, Level = LogLevel.Information, Message = "Deployment {DeploymentId} of {ImageName} on machine {MachineId} cancelled")]
    public static partial void Cancelled(ILogger logger, Guid deploymentId, string imageName, Guid machineId);

    [LoggerMessage(EventId = 955, Level = LogLevel.Warning, Message = "Deployment {DeploymentId} of {ImageName} failed on machine {MachineId} before it started: {Error}")]
    public static partial void FailedBeforeStart(ILogger logger, Guid deploymentId, string imageName, Guid machineId, string? error);

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
                Assigned(logger, deployment.Id, deployment.ImageName, deployment.MachineId, deployment.RequestedByName ?? "unknown");
                break;
            case DeploymentState.Running:
                Started(logger, deployment.Id, deployment.ImageName, deployment.MachineId);
                break;
            case DeploymentState.Done:
                Done(logger, deployment.Id, deployment.ImageName, deployment.MachineId);
                break;
            case DeploymentState.Failed when deployment.Step is null:
                FailedBeforeStart(logger, deployment.Id, deployment.ImageName, deployment.MachineId, deployment.Error);
                break;
            case DeploymentState.Failed:
                Failed(logger, deployment.Id, deployment.ImageName, deployment.MachineId, deployment.Step, deployment.Error);
                break;
            case DeploymentState.Cancelled:
                Cancelled(logger, deployment.Id, deployment.ImageName, deployment.MachineId);
                break;
        }
    }
}
