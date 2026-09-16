using DDT.Contracts.Machines;

namespace DDT.Server.Machines;

public static class MachineSummaries
{
    public static MachineSummary From(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return new MachineSummary(
            machine.Id,
            machine.State,
            machine.SmbiosUuid,
            machine.PrimaryMac,
            machine.MacAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries),
            machine.Manufacturer,
            machine.Model,
            machine.SerialNumber,
            machine.AssignedName,
            machine.AgentVersion,
            machine.FirstSeenUtc,
            machine.LastSeenUtc,
            machine.LastSeenAddress);
    }
}
