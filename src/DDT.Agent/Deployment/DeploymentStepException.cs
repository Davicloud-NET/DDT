namespace DDT.Agent.Deployment;

// A deployment step cannot go on. The message is a sentence for the operator and becomes the deployment's error.
public sealed class DeploymentStepException : Exception
{
    public DeploymentStepException()
    {
    }

    public DeploymentStepException(string message)
        : base(message)
    {
    }

    public DeploymentStepException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
