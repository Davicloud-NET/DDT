namespace DDT.Contracts.Agents;

// Typed by the technician at the machine. TwoFactorCode is sent once the server has asked for it.
public sealed record AgentSignInRequest(string UserName, string Password, string? TwoFactorCode);
