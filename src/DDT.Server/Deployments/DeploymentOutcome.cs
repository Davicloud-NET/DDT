namespace DDT.Server.Deployments;

public enum DeploymentOutcome
{
    Accepted,

    // A repeated terminal report: nothing to save, and the agent gets its tokens as if it had been.
    Unchanged,
    NotFound,
    Conflict,
    Invalid,
}
