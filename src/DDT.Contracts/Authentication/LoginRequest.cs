namespace DDT.Contracts.Authentication;

public sealed record LoginRequest(string UserName, string Password, string? TwoFactorCode, string? RecoveryCode);
