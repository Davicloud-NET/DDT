namespace DDT.Server.Machines;

public static class MachineTokenLifetimes
{
    // Short enough that a stolen token is worth little, long enough that a multi gigabyte image
    // copy never depends on one token surviving: the agent refreshes instead.
    public static readonly TimeSpan Session = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan Poll = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan ImageGrant = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan SecretGrant = TimeSpan.FromMinutes(5);
}
