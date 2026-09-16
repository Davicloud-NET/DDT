using DDT.Contracts.Machines;

namespace DDT.Contracts.Agents;

// Token is a poll token while the machine waits for approval and a session token once approved. Both
// tokens are null when the machine has been rejected: it gets nothing further to present.
public sealed record AgentRegistrationResult(
    Guid MachineId,
    MachineState State,
    string? Token,
    string? ResumeToken,
    int PollAfterSeconds);
