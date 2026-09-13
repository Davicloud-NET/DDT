using System.Net;

namespace DDT.Pxe;

// Address is the one DDT hands out when a request does not say which address the client used, such
// as a broadcast DISCOVER. Addresses holds every IPv4 address on the interface, because option 66 at a
// site or an HTTP boot URL may name a secondary one.
public sealed record ServedInterface(int Index, string Name, IReadOnlyList<IPAddress> Addresses)
{
    public ServedInterface(int index, string name, IPAddress address)
        : this(index, name, [address])
    {
    }

    public IPAddress Address => Addresses[0];

    public bool Owns(IPAddress address) => Addresses.Contains(address);
}
