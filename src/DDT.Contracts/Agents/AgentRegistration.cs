namespace DDT.Contracts.Agents;

// ResumeToken is the one the agent was last given, if any. It proves the registration comes from the
// agent that already holds this machine, so its approval survives an expired token or an outage.
public sealed record AgentRegistration(
    string SmbiosUuid,
    string PrimaryMac,
    IReadOnlyList<string> MacAddresses,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string AgentVersion,
    string? ResumeToken = null);
