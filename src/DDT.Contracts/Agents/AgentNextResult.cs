using DDT.Contracts.Machines;

namespace DDT.Contracts.Agents;

// Every poll hands out fresh tokens, so they only expire when the agent cannot reach the server.
public sealed record AgentNextResult(
    MachineState State,
    string Token,
    string ResumeToken,
    int PollAfterSeconds,
    string? SignedInBy);
