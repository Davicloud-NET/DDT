using DDT.Contracts.Agents;

namespace DDT.Server.Machines;

public sealed class MachineLogLine
{
    public long Id { get; set; }

    public Guid MachineId { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }

    public DateTimeOffset ReceivedUtc { get; set; }

    public AgentLogLevel Level { get; set; }

    public required string Message { get; set; }
}
