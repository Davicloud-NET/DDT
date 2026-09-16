namespace DDT.Agent;

// The server answered 401: the token expired, or the machine was re-registered, rejected or retired.
public sealed class AgentTokenRejectedException : Exception
{
    public AgentTokenRejectedException()
    {
    }

    public AgentTokenRejectedException(string message)
        : base(message)
    {
    }

    public AgentTokenRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
