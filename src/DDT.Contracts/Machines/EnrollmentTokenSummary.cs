namespace DDT.Contracts.Machines;

public sealed record EnrollmentTokenSummary(
    Guid Id,
    string Name,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc,
    DateTimeOffset? RevokedUtc);
