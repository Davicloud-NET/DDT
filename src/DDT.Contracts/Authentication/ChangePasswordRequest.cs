namespace DDT.Contracts.Authentication;

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
