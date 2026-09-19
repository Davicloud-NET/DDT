using DDT.Contracts.Deployments;

namespace DDT.Agent.Deployment;

// Reported synchronously, unlike Progress<T>, so the heartbeat never sends a percent older than the last one.
internal sealed class StepProgress(DeploymentHeartbeat heartbeat, DeploymentStep step) : IProgress<int>
{
    public void Report(int value) => heartbeat.Progress(step, value);
}
