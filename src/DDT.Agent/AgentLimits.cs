namespace DDT.Agent;

public static class AgentLimits
{
    // Matches the server's cap on one log request.
    public const int MaxLinesPerBatch = 200;

    // Matches the server's cap on one registration.
    public const int MaxMacAddresses = 16;

    public static readonly TimeSpan MinRetryDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    // Covers the wait in the server's queue too: a whole lab downloads the agent at once over a site link, well
    // beyond the 30 seconds a request gets.
    public static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);

    public static TimeSpan RetryDelay(int failures)
    {
        TimeSpan delay = MinRetryDelay * (1 << Math.Clamp(failures - 1, 0, 4));

        return delay < MaxRetryDelay ? delay : MaxRetryDelay;
    }
}
