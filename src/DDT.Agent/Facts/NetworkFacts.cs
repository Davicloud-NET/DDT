// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DDT.Agent.Facts;

// The primary adapter's IPv4 settings: its address with that address's prefix length, its default gateway, its
// connection-specific DNS suffix and the DHCP server that gave it its lease. Each is null where the adapter has none.
public sealed record NetworkFacts(string? IPv4Address, int? PrefixLength, string? DefaultGateway, string? DnsSuffix, string? DhcpServer)
{
    public static NetworkFacts Read(IPInterfaceProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        return From(
            properties.UnicastAddresses.Select(unicast => (unicast.Address, unicast.PrefixLength)),
            properties.GatewayAddresses.Select(gateway => gateway.Address),
            properties.DnsSuffix,
            properties.DhcpServerAddresses);
    }

    // The first address that is not link-local, which Windows gives itself while no DHCP server answers, unless there is
    // no other. Unset addresses and the broadcast address, which Windows can report for a gateway or a DHCP server that
    // is not there, count as none.
    public static NetworkFacts From(
        IEnumerable<(IPAddress Address, int PrefixLength)> unicast,
        IEnumerable<IPAddress> gateways,
        string? dnsSuffix,
        IEnumerable<IPAddress> dhcpServers)
    {
        ArgumentNullException.ThrowIfNull(unicast);
        ArgumentNullException.ThrowIfNull(gateways);
        ArgumentNullException.ThrowIfNull(dhcpServers);

        // OrderBy keeps the adapter's order among addresses of one kind.
        (IPAddress Address, int PrefixLength)[] addresses =
            [.. unicast.Where(entry => IsSet(entry.Address)).OrderBy(entry => IsLinkLocal(entry.Address))];
        (IPAddress Address, int PrefixLength)? address = addresses.Length > 0 ? addresses[0] : null;

        return new NetworkFacts(
            address?.Address.ToString(),
            address?.PrefixLength is >= 0 and <= 32 ? address.Value.PrefixLength : null,
            gateways.FirstOrDefault(IsSet)?.ToString(),
            string.IsNullOrWhiteSpace(dnsSuffix) ? null : dnsSuffix.Trim(),
            dhcpServers.FirstOrDefault(IsSet)?.ToString());
    }

    private static bool IsSet(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetwork && !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.Broadcast);

    // 169.254.0.0/16.
    private static bool IsLinkLocal(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();

        return bytes[0] == 169 && bytes[1] == 254;
    }
}
