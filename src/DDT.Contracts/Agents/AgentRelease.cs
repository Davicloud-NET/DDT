namespace DDT.Contracts.Agents;

// Frozen: agents inside boot images built long ago read this, so its members are never renamed or removed.
public sealed record AgentRelease(string Sha256, long Size);
