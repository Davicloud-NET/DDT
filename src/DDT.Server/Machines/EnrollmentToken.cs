namespace DDT.Server.Machines;

// The only credential Windows PE carries, and therefore public to anyone who can download boot.wim.
// Its sole power is to register a machine, which then waits for approval.
public sealed class EnrollmentToken
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required byte[] SecretHash { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset ExpiresUtc { get; set; }

    public DateTimeOffset? RevokedUtc { get; set; }

    public Guid? CreatedByUserId { get; set; }
}
