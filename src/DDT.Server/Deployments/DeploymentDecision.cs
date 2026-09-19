namespace DDT.Server.Deployments;

// Reason is a sentence for the operator or the technician; Field names the request member an Invalid refers to.
public sealed record DeploymentDecision(DeploymentOutcome Outcome, Deployment? Deployment, string? Reason, string? Field)
{
    public static DeploymentDecision Accepted(Deployment deployment) => new(DeploymentOutcome.Accepted, deployment, null, null);

    public static DeploymentDecision Unchanged(Deployment deployment) => new(DeploymentOutcome.Unchanged, deployment, null, null);

    public static DeploymentDecision NotFound(string reason) => new(DeploymentOutcome.NotFound, null, reason, null);

    public static DeploymentDecision Conflict(string reason) => new(DeploymentOutcome.Conflict, null, reason, null);

    public static DeploymentDecision Invalid(string field, string reason) => new(DeploymentOutcome.Invalid, null, reason, field);
}
