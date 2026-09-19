namespace DDT.Contracts.Agents;

public sealed record AgentDisk(int Number, string? Model, long SizeBytes, string BusType, int PartitionCount);
