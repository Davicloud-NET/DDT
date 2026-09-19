using System.Net;

namespace DDT.Server.Deployments;

// DDT:Machines:ZeroTouchNetworks, parsed once at startup so that a typo stops the server instead of silently
// turning zero touch off.
public sealed class ZeroTouchNetworks
{
    private readonly IPNetwork[] _networks;

    private ZeroTouchNetworks(IPNetwork[] networks)
    {
        _networks = networks;
    }

    public static ZeroTouchNetworks Parse(string? value)
    {
        List<IPNetwork> networks = [];
        List<string> problems = [];

        foreach (string entry in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Network(entry) is { } network)
            {
                networks.Add(network);
            }
            else
            {
                problems.Add(
                    $"DDT:Machines:ZeroTouchNetworks contains '{entry}', which is not a network. Write each one as an address " +
                    "and a prefix length with no address bits set after the prefix, such as 10.20.0.0/16, fd00:20::/64 " +
                    "or 10.20.1.5/32 for one machine.");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, problems));
        }

        return new ZeroTouchNetworks([.. networks]);
    }

    // IPNetwork.Parse clears address bits after the prefix. Such an entry is refused instead: 10.20.30.40/16 more
    // likely lacks a digit in its prefix than means all of 10.20.0.0/16.
    private static IPNetwork? Network(string entry)
    {
        int slash = entry.IndexOf('/', StringComparison.Ordinal);

        return IPNetwork.TryParse(entry, out IPNetwork network)
            && slash > 0
            && IPAddress.TryParse(entry.AsSpan(0, slash), out IPAddress? written)
            && written.Equals(network.BaseAddress)
                ? network
                : null;
    }

    public bool IsEmpty => _networks.Length == 0;

    // A dual-stack socket reports an IPv4 client as ::ffff:a.b.c.d, which no IPv4 network contains.
    public bool Contains(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        IPAddress client = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        return _networks.Any(network => network.Contains(client));
    }
}
