namespace DDT.Server.Machines;

public static class MachineTokenLifetimes
{
    // Short enough that a stolen token is worth little, long enough that a multi gigabyte image
    // copy never depends on one token surviving: the agent refreshes instead.
    public static readonly TimeSpan Session = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan Poll = TimeSpan.FromMinutes(60);

    // How long the server may be unreachable before an approved machine has to be approved again.
    public static readonly TimeSpan Resume = TimeSpan.FromHours(24);
}
