namespace DDT.Contracts.Authentication;

public sealed record TwoFactorEnrollment(string SharedKey, string AuthenticatorUri);
