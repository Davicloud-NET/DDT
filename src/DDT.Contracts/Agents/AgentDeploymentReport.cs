using DDT.Contracts.Deployments;

namespace DDT.Contracts.Agents;

public sealed record AgentDeploymentReport(DeploymentState State, DeploymentStep Step, int Percent, string? Error);
