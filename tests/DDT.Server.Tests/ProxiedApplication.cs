using Microsoft.AspNetCore.Hosting;

namespace DDT.Server.Tests;

public sealed class ProxiedApplication : DdtApplication
{
    public const string Proxy = "192.0.2.10";

    public const string Ipv6Proxy = "2001:db8::10";

    public const string ProxyNetwork = "198.51.100.0/24";

    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("DDT:ForwardedHeaders:KnownProxies", $"{Proxy}, {Ipv6Proxy}");
        builder.UseSetting("DDT:ForwardedHeaders:KnownNetworks", ProxyNetwork);
    }
}
