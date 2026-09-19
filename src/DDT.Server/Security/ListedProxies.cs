using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;

namespace DDT.Server.Security;

// The addresses DDT:ForwardedHeaders trusts. A request that still comes from one after the middleware ran carried no
// client address the proxy added.
public sealed class ListedProxies(IOptionsMonitor<ForwardedHeadersOptions> options)
{
    // Matched as the middleware matches them, so an IPv4 client on a dual-stack socket finds its IPv4 entry.
    public bool Contains(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        ForwardedHeadersOptions trusted = options.Get(DdtForwardedHeadersExtensions.OptionsName);

        return Lists(trusted, address) || (address.IsIPv4MappedToIPv6 && Lists(trusted, address.MapToIPv4()));
    }

    private static bool Lists(ForwardedHeadersOptions trusted, IPAddress address) =>
        trusted.KnownProxies.Contains(address) || trusted.KnownIPNetworks.Any(network => network.Contains(address));
}
