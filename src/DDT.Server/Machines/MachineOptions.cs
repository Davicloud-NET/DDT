namespace DDT.Server.Machines;

public sealed class MachineOptions
{
    public const string SectionName = "DDT:Machines";

    // Off, a technician signing in at the machine authorizes it. On, that sign-in only records who is at the
    // machine, and an operator also has to approve it on the web.
    public bool RequireWebApproval { get; init; }

    // Registration is open to anyone who reaches the server, so machines nobody has approved yet are capped per
    // address, which a lab behind one NAT address still fits in, and in total.
    public int MaxWaitingPerAddress { get; init; } = 100;

    public int MaxWaiting { get; init; } = 10_000;

    // Comma separated networks in CIDR notation. A machine assigned an image on the web that netboots from one of
    // them later stays authorized. Matched against the client address, which behind a proxy listed in
    // DDT:ForwardedHeaders is the one the proxy reports, so no network may contain a reverse proxy's own address.
    // Empty turns zero touch off.
    public string ZeroTouchNetworks { get; init; } = string.Empty;
}
