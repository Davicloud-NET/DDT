namespace DDT.Agent.Deployment;

public sealed record StorageDeviceInfo(bool RemovableMedia, StorageBusType BusType, string? Model);
