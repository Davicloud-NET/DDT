namespace DDT.Contracts.Authentication;

public sealed record CurrentUser(
    Guid Id,
    string UserName,
    string? DisplayName,
    string Source,
    bool TwoFactorEnabled,
    IReadOnlyList<string> Roles);
