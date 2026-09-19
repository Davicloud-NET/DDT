namespace DDT.Agent;

// Frozen range: an agent from an older boot image starts newer ones and passes their exit codes on, and takes any
// code outside 0 to HighestAgentCode, or ConfigurationError, to mean the new agent could not run at all. So
// ConfigurationError may only ever mean that the arguments or agent.json could not be read.
public static class AgentExitCodes
{
    public const int Stopped = 0;
    public const int Rejected = 2;
    public const int ConfigurationError = 3;
    public const int Deployed = 4;
    public const int HighestAgentCode = 63;
}
