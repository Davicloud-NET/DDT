namespace DDT.Server.Machines;

// Disks and EligibleDiskCount are null when the agent is too old to report its disks.
public sealed record NormalisedRegistration(
    string SmbiosUuid,
    string PrimaryMac,
    IReadOnlyList<string> MacAddresses,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string AgentVersion,
    string? ResumeToken,
    string? Disks,
    int? EligibleDiskCount);
