namespace DDT.Contracts.Agents;

public sealed record AgentLogLine(DateTimeOffset TimestampUtc, AgentLogLevel Level, string Message);
