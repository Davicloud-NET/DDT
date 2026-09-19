namespace DDT.Contracts.Agents;

// A running deployment does not poll next, so every report hands out the tokens a poll would.
public sealed record AgentDeploymentReportResult(string Token, string ResumeToken);
