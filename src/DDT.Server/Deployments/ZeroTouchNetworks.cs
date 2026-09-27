// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Core.Configuration;
using DDT.Server.Machines;

namespace DDT.Server.Deployments;

// The zero touch networks of the machines section, parsed once per snapshot, so that a typo is refused on save rather
// than silently turning zero touch off.
public sealed class ZeroTouchNetworks
{
    private readonly IPNetwork[] _networks;

    private ZeroTouchNetworks(IPNetwork[] networks)
    {
        _networks = networks;
    }

    // Zero touch off.
    public static ZeroTouchNetworks None { get; } = new([]);

    public static IReadOnlyList<SettingProblem> FindProblems(string? value) =>
    [
        .. Entries(value)
            .Where(entry => Network(entry) is null)
            .Select(entry => new SettingProblem(
                "ZeroTouchNetworks",
                $"'{entry}' is not a network. Write each one as an address and a prefix length with no address bits set " +
                "after the prefix, such as 10.20.0.0/16, fd00:20::/64 or 10.20.1.5/32 for one machine.")),
        .. Entries(value)
            .Where(entry => Network(entry) is { PrefixLength: 0 })
            .Select(entry => new SettingProblem(
                "ZeroTouchNetworks",
                $"'{entry}' is every address there is. Name the provisioning networks themselves.")),
    ];

    public static ZeroTouchNetworks Parse(string? value)
    {
        SettingProblem.ThrowIfAny(MachineOptions.SectionName, FindProblems(value));

        return new ZeroTouchNetworks([.. Networks(value)]);
    }

    // The entries that are networks, for the rules that compare them with other networks.
    public static IReadOnlyList<IPNetwork> Networks(string? value) =>
        [.. Entries(value).Select(Network).OfType<IPNetwork>()];

    private static string[] Entries(string? value) =>
        (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
