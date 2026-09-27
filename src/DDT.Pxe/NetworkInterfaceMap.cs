// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DDT.Pxe;

// Datagrams arrive on a wildcard socket, so the only thing identifying the segment is the interface
// index from IP_PKTINFO. This resolves that index to a local address for siaddr and option 54, and
// it is what enforces the configured allowlist: an index that is not served is never answered.
//
// A snapshot taken whenever the pxe settings are applied. A changed address is picked up by the next apply, which a
// save of the section or its Rescan action starts.
public sealed class NetworkInterfaceMap
{
    private readonly FrozenDictionary<int, ServedInterface> _served;

    public NetworkInterfaceMap(string configured, IReadOnlyList<ServedInterface> candidates)
    {
        ArgumentNullException.ThrowIfNull(configured);
        ArgumentNullException.ThrowIfNull(candidates);

        Dictionary<int, ServedInterface> served = [];
        List<string> unmatched = [];

        foreach (string wanted in configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            bool matched = false;

            foreach (ServedInterface candidate in candidates)
            {
                if (Matches(candidate, wanted))
                {
                    served[candidate.Index] = candidate;
                    matched = true;
                }
            }

            if (!matched)
            {
                unmatched.Add(wanted);
            }
        }

        _served = served.ToFrozenDictionary();
        Candidates = candidates;
        Unmatched = unmatched;
    }

    public IReadOnlyList<ServedInterface> Candidates { get; }

    public IReadOnlyList<ServedInterface> Served => _served.Values;

    public IReadOnlyList<string> Unmatched { get; }

    public static NetworkInterfaceMap FromHost(string configured) => new(configured, Enumerate());

    public bool TryGetInterface(int interfaceIndex, [NotNullWhen(true)] out ServedInterface? served) =>
        _served.TryGetValue(interfaceIndex, out served);

    public bool IsServedAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        IPAddress version4 = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        foreach (ServedInterface served in _served.Values)
        {
            if (served.Owns(version4))
            {
                return true;
            }
        }

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

            IPAddress[] addresses =
            [
                .. properties.UnicastAddresses
                    .Select(unicast => unicast.Address)
                    .Where(address => address.AddressFamily == AddressFamily.InterNetwork),
            ];

            if (addresses.Length > 0)
            {
                candidates.Add(new ServedInterface(version4.Index, adapter.Name, addresses));
            }
        }

        return candidates;
    }

    private static bool Matches(ServedInterface candidate, string wanted) =>
        string.Equals(candidate.Name, wanted, StringComparison.OrdinalIgnoreCase)
        || candidate.Addresses.Any(address => string.Equals(address.ToString(), wanted, StringComparison.Ordinal));
}
