namespace DDT.Server.Data;

public sealed class AuditEvent
{
    public long Id { get; set; }

    public DateTimeOffset OccurredUtc { get; set; }

    public required string Action { get; set; }

    public Guid? ActorUserId { get; set; }

    public Guid? ActorMachineId { get; set; }

    public string? ActorName { get; set; }

    public string? SubjectId { get; set; }

    public string? SourceAddress { get; set; }

    public string? Detail { get; set; }
}
