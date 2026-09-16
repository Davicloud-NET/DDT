namespace DDT.Contracts.Agents;

public enum AgentSignInStatus
{
    Succeeded,
    RequiresTwoFactor,
    Failed,
    LockedOut,
    NotPermitted,
    AlreadyDecided,
}
