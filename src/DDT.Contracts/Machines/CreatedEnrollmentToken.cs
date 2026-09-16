namespace DDT.Contracts.Machines;

// Token is shown once. The server keeps only a hash of it.
public sealed record CreatedEnrollmentToken(EnrollmentTokenSummary Summary, string Token);
