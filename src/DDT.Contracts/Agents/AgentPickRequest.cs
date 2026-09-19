namespace DDT.Contracts.Agents;

public sealed record AgentPickRequest(Guid ImageId, int? DiskNumber, string? ComputerName);
