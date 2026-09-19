namespace DDT.Agent.Deployment;

public enum DeploymentOutcome
{
    // Windows is installed and the machine restarts into it.
    Deployed,

    // The deployment failed or could not start; the machine keeps polling with the tokens handed back.
    Failed,

    // The server refused the machine's token, so the agent registers again.
    TokenRejected,

    // The agent was asked to stop.
    Stopped,
}
