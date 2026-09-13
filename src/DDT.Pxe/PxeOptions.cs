namespace DDT.Pxe;

public sealed class PxeOptions
{
    public const string SectionName = "DDT:Pxe";

    // Comma separated interface names or IPv4 addresses of local interfaces. There is deliberately no
    // default: a host may carry a NIC on a segment whose DHCP belongs to someone else, and DDT must
    // not answer PXE there. Unset means serve nothing.
    public string Interfaces { get; init; } = string.Empty;

    public string BootDirectory { get; init; } = "/var/lib/ddt/boot";

    public int HttpBootPort { get; init; } = 8080;

    // A site whose own DHCP server points next-server and bootfile at DDT needs no ProxyDHCP, and
    // turning it off means DDT binds nothing on UDP 67 or 4011.
    public bool EnableProxyDhcp { get; init; } = true;

    public bool EnableTftp { get; init; } = true;

    // Replies from port 69 instead of a fresh port per transfer. The fix when a read request reaches
    // DDT but the client never receives data, because a stateful firewall drops the reply.
    public bool TftpSinglePort { get; init; }

    // Only 4 has Microsoft backing for bootmgr. DDT writes the BCD, so larger windows can be measured.
    public int TftpMaxWindowSize { get; init; } = 4;

    // Comma separated rather than a list, for the same reason as DDT:Roles.
    public string AuthorisedRelayAgents { get; init; } = string.Empty;

    public int MaxConcurrentTftpTransfers { get; init; } = 128;

    // Keyed by ClientArchitecture member name as a string. The binder silently drops a dictionary key
    // it cannot convert to an enum, which would leave a machine at a blank screen with no error.
    public Dictionary<string, BootTargetOptions> BootTargets { get; } = new(StringComparer.OrdinalIgnoreCase);
}
