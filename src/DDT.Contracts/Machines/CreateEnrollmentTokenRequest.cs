namespace DDT.Contracts.Machines;

public sealed record CreateEnrollmentTokenRequest(string Name, int ValidForDays);
