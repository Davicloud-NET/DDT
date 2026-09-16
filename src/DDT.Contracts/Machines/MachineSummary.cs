namespace DDT.Contracts.Machines;

public sealed record MachineSummary(
    Guid Id,
    MachineState State,
    string SmbiosUuid,
    string PrimaryMac,
    IReadOnlyList<string> MacAddresses,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? AssignedName,
    string? AgentVersion,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    string? LastSeenAddress);
