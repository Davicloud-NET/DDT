using DDT.Contracts.Deployments;

namespace DDT.Contracts.Agents;

// DiskNumber is the disk chosen at the machine; a deployment assigned on the web has none.
public sealed record AgentDeployment(
    Guid Id,
    DeploymentState State,
    Guid ImageId,
    string ImageName,
    string Sha256,
    long SizeBytes,
    int WimIndex,
    long InstalledBytes,
    int? DiskNumber);
