using DDT.Contracts.Deployments;

namespace DDT.Agent.Deployment;

// Token and ResumeToken are the latest the run was given. Step and Percent are where the run ended. UnsentError is
// the failure the server could not be told about, for the loop to report once it can.
public sealed record DeploymentRunResult(
    DeploymentOutcome Outcome,
    string Token,
    string ResumeToken,
    DeploymentStep Step,
    int Percent,
    string? UnsentError);
