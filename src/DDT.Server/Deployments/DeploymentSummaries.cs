using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;

namespace DDT.Server.Deployments;

public static class DeploymentSummaries
{
    public static DeploymentSummary From(Deployment deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return new DeploymentSummary(
            deployment.Id,
            deployment.ImageId,
            deployment.ImageName,
            deployment.State,
            deployment.Step,
            deployment.Percent,
            deployment.Source,
            deployment.RequestedByName,
            deployment.CreatedUtc,
            deployment.StartedUtc,
            deployment.FinishedUtc,
            deployment.Error);
    }

    // An image cannot be deleted while a deployment that is assigned or running uses it, so those always have one.
    public static AgentDeployment ForAgent(Deployment deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return new AgentDeployment(
            deployment.Id,
            deployment.State,
            deployment.ImageId ?? Guid.Empty,
            deployment.ImageName,
            deployment.Sha256,
            deployment.SizeBytes,
            deployment.WimIndex,
            deployment.InstalledBytes,
            deployment.DiskNumber);
    }
}
