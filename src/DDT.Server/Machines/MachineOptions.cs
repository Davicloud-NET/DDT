namespace DDT.Server.Machines;

public sealed class MachineOptions
{
    public const string SectionName = "DDT:Machines";

    // Off, a technician signing in at the machine authorizes it. On, that sign-in only records who is at the
    // machine, and an operator also has to approve it on the web.
    public bool RequireWebApproval { get; init; }
}
