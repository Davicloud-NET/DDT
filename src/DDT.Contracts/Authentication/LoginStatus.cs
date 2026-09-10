namespace DDT.Contracts.Authentication;

public enum LoginStatus
{
    Succeeded,
    RequiresTwoFactor,
    LockedOut,
    Failed,
}
