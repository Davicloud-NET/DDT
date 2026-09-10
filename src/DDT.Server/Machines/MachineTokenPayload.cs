namespace DDT.Server.Machines;

public sealed record MachineTokenPayload(
    Guid MachineId,
    string SmbiosUuid,
    string PrimaryMac,
    int TokenGeneration,
    string? Resource);
