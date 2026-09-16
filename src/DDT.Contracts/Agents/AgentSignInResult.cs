namespace DDT.Contracts.Agents;

// Every outcome of the credentials is a 200 with a status. A 401 always means the machine token was refused.
public sealed record AgentSignInResult(AgentSignInStatus Status);
