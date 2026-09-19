namespace DDT.Server.Security;

public static class RateLimitPolicies
{
    public const string SignIn = "ddt.signin";
    public const string AgentRegistration = "ddt.agent.registration";
    public const string AgentMachine = "ddt.agent.machine";
    public const string AgentRelease = "ddt.agent.release";
    public const string AgentDownload = "ddt.agent.download";
}
