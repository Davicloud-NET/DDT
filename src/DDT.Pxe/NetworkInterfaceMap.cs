using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DDT.Pxe;

// Datagrams arrive on a wildcard socket, so the only thing identifying the segment is the interface
// index from IP_PKTINFO. This resolves that index to a local address for siaddr and option 54, and
// it is also what enforces the configured allowlist: an index that is not served is never answered.
public sealed class NetworkInterfaceMap
{
    private readonly Dictionary<int, ServedInterface> _served = [];

    public NetworkInterfaceMap(string configured)
    {
        ArgumentNullException.ThrowIfNull(configured);

        string[] wanted = configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (ServedInterface candidate in Enumerate())
        {
            if (wanted.Any(name => Matches(candidate, name)))
            {
                _served[candidate.Index] = candidate;
            }
        }
    }

    public IReadOnlyCollection<ServedInterface> Served => _served.Values;

    public bool IsServed(int interfaceIndex) => _served.ContainsKey(interfaceIndex);

    public bool TryGetAddress(int interfaceIndex, out IPAddress address)
    {
        if (_served.TryGetValue(interfaceIndex, out ServedInterface? served))
        {
            address = served.Address;

            return true;
        }

        address = IPAddress.None;

        return false;
    }

    public static IReadOnlyList<ServedInterface> Enumerate()
    {
        List<ServedInterface> candidates = [];

        foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up
                || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            IPInterfaceProperties properties = adapter.GetIPProperties();
            IPv4InterfaceProperties? version4;

            try
            {
                version4 = properties.GetIPv4Properties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            if (version4 is null)
            {
                continue;
            }

            foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    candidates.Add(new ServedInterface(version4.Index, adapter.Name, unicast.Address));
                    break;
                }
            }
        }

        return candidates;
    }

    private static bool Matches(ServedInterface candidate, string wanted) =>
        string.Equals(candidate.Name, wanted, StringComparison.OrdinalIgnoreCase)
        || string.Equals(candidate.Address.ToString(), wanted, StringComparison.Ordinal);
}
