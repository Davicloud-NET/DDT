namespace DDT.Server.Tests;

// The zero touch networks of ZeroTouchApplication, reached through the proxies of ProxiedApplication.
public sealed class ProxiedZeroTouchApplication() : SettingsApplication(
    ("DDT:Machines:ZeroTouchNetworks", ZeroTouchApplication.Networks),
    ("DDT:ForwardedHeaders:KnownProxies", $"{ProxiedApplication.Proxy}, {ProxiedApplication.Ipv6Proxy}"),
    ("DDT:ForwardedHeaders:KnownNetworks", ProxiedApplication.ProxyNetwork));
