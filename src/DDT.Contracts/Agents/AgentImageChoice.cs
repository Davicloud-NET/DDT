namespace DDT.Contracts.Agents;

public sealed record AgentImageChoice(Guid Id, string Name, string? Edition, string? Language, long SizeBytes, long InstalledBytes);
