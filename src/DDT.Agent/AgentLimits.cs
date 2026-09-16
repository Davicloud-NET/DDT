namespace DDT.Agent;

public static class AgentLimits
{
    // Matches the server's cap on one log request.
    public const int MaxLinesPerBatch = 200;

    // Matches the server's cap on one registration.
    public const int MaxMacAddresses = 16;

    public static readonly TimeSpan MinRetryDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);
}
