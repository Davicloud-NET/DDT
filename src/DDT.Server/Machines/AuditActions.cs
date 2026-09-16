namespace DDT.Server.Machines;

public static class AuditActions
{
    public const string MachineRegistered = "machine.registered";
    public const string MachineReregistered = "machine.reregistered";
    public const string MachineApproved = "machine.approved";
    public const string MachineRejected = "machine.rejected";
    public const string EnrollmentTokenCreated = "enrollment-token.created";
    public const string EnrollmentTokenRevoked = "enrollment-token.revoked";
}
