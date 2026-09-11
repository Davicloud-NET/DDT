namespace DDT.Pxe;

public sealed class PxeOptions
{
    public const string SectionName = "DDT:Pxe";

    // Comma separated interface names, or IPv4 addresses of local interfaces. There is deliberately
    // no default: a host may carry a management NIC on a production segment, and DDT answering PXE
    // there would compete with whatever already serves it. Unset means serve nothing.
    public string Interfaces { get; init; } = string.Empty;

    public string BootDirectory { get; init; } = "/var/lib/ddt/boot";

    public int HttpBootPort { get; init; } = 8080;

    public string AuthorisedRelayAgents { get; init; } = string.Empty;

    public bool EnableTftp { get; init; } = true;

    public int MaxConcurrentTftpTransfers { get; init; } = 64;

    public IList<BootTargetOptions> BootTargets { get; init; } = [];
}
