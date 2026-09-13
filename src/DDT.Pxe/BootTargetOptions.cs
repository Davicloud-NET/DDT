namespace DDT.Pxe;

// Every leaf is a string so a bad value becomes a named startup failure in PxeSetup. The binder
// leaves an unparseable IPAddress null without complaint, and an entry created by one environment
// variable would otherwise default its Method to Tftp.
public sealed class BootTargetOptions
{
    public string? Method { get; init; }

    public string? BootFile { get; init; }

    public string? ServerAddress { get; init; }

    public string? ServerHostName { get; init; }

    public bool AdvertiseBootServerDiscovery { get; init; }
}
