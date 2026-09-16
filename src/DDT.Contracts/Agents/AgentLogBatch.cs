namespace DDT.Contracts.Agents;

public sealed record AgentLogBatch(IReadOnlyList<AgentLogLine> Lines);
