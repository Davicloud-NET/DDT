using DDT.Protocols.Dhcp;
using DDT.Protocols.Pxe;

namespace DDT.Pxe;

public sealed class BootTargetOptions
{
    public ClientArchitecture Architecture { get; init; }

    public BootMethod Method { get; init; } = BootMethod.Tftp;

    public string BootFile { get; init; } = string.Empty;

    public string? ServerHostName { get; init; }

    public bool AdvertiseBootServerDiscovery { get; init; }
}
