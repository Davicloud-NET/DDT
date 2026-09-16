using DDT.Contracts.Agents;

namespace DDT.Contracts.Machines;

// TimestampUtc is the agent's clock, which in Windows PE may be wrong. ReceivedUtc is the server's.
public sealed record MachineLogEntry(
    long Id,
    DateTimeOffset TimestampUtc,
    DateTimeOffset ReceivedUtc,
    AgentLogLevel Level,
    string Message);
