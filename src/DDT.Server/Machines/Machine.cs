using DDT.Contracts.Machines;
using DDT.Server.Data;

namespace DDT.Server.Machines;

public sealed class Machine
{
    public Guid Id { get; set; }

    // Reported by the agent and therefore attacker controllable. Used to recognise a machine
    // across reboots, never as proof of identity.
    public required string SmbiosUuid { get; set; }

    public required string PrimaryMac { get; set; }

    // Comma separated, normalised to twelve upper case hex digits each.
    public string MacAddresses { get; set; } = string.Empty;

    public string? Manufacturer { get; set; }

    public string? Model { get; set; }

    public string? SerialNumber { get; set; }

    // Set by an administrator, not by the agent, because it reaches a domain join command line.
    public string? AssignedName { get; set; }

    public string? AgentVersion { get; set; }

    public MachineState State { get; set; } = MachineState.Pending;

    // Every issued machine token carries the generation it was minted under. Bumping this
    // invalidates all outstanding tokens for the machine without tracking them individually.
    public int TokenGeneration { get; set; }

    public DateTimeOffset FirstSeenUtc { get; set; }

    public DateTimeOffset LastSeenUtc { get; set; }

    public string? FirstSeenAddress { get; set; }

    public string? LastSeenAddress { get; set; }

    public string? EnrollmentTokenId { get; set; }

    public Guid? ApprovedByUserId { get; set; }

    public DdtUser? ApprovedBy { get; set; }

    public DateTimeOffset? ApprovedUtc { get; set; }

    public Guid? SignedInByUserId { get; set; }

    // A copy of the user name at the time, so the machines list and the live push need no join.
    public string? SignedInUserName { get; set; }

    public DateTimeOffset? SignedInUtc { get; set; }
}
