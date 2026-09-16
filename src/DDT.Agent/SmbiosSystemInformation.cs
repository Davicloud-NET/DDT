namespace DDT.Agent;

public sealed record SmbiosSystemInformation(Guid Uuid, string? Manufacturer, string? ProductName, string? SerialNumber);
