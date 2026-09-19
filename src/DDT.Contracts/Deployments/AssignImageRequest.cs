namespace DDT.Contracts.Deployments;

public sealed record AssignImageRequest(Guid ImageId, string? ComputerName);
