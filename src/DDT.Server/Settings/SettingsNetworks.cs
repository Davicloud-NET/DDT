// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using DDT.Contracts.Messages;
using DDT.Core.Configuration;
using DDT.Server.Deployments;
using DDT.Server.Security;

namespace DDT.Server.Settings;

// The rules that compare networks. They check how wide a network may be without a confirmation, and that no zero touch
// network holds a trusted proxy.
internal static class SettingsNetworks
{
    private const int WidestVersion4 = 16;
    private const int WidestVersion6 = 48;

    // Wide says what an address in the network counts as. It gets the network and the widest prefix allowed.
    public static IReadOnlyList<SettingWarning> Wide(IEnumerable<IPNetwork> networks, string field, MessageTemplate wide) =>
    [
        .. networks
            .Select(network => (Network: network, Widest: network.BaseAddress.AddressFamily == AddressFamily.InterNetwork ? WidestVersion4 : WidestVersion6))
            .Where(entry => entry.Network.PrefixLength < entry.Widest)
            .Select(entry => new SettingWarning(
                field,
                wide.With("network", entry.Network.ToString(), "prefix", entry.Widest),
                SettingWarningCodes.WideNetwork)),
    ];

    // When a proxy forwards a request without the client's address, the request comes from the proxy's own address. A
    // zero touch network must not hold that address. Field names the zero touch field for a problem of the machines
    // section. Null puts the problem on the proxies field.
    public static IReadOnlyList<SettingProblem> Overlaps(string zeroTouchNetworks, DdtForwardedHeadersOptions proxies, string? field)
    {
        List<SettingProblem> problems = [];
        IReadOnlyList<IPNetwork> zeroTouch = ZeroTouchNetworks.Networks(zeroTouchNetworks);

        foreach (IPAddress proxy in DdtForwardedHeadersExtensions.Proxies(proxies.KnownProxies))
        {
            foreach (IPNetwork network in zeroTouch.Where(network => network.Contains(proxy)))
            {
                problems.Add(new(
                    field ?? "KnownProxies",
                    ServerMessages.SettingsMachinesNetworkHoldsProxy.With("network", network.ToString(), "proxy", proxy.ToString())));
            }
        }

        foreach (IPNetwork listed in DdtForwardedHeadersExtensions.Networks(proxies.KnownNetworks))
        {
            foreach (IPNetwork network in zeroTouch.Where(network => network.Contains(listed.BaseAddress) || listed.Contains(network.BaseAddress)))
            {
                problems.Add(new(
                    field ?? "KnownNetworks",
                    ServerMessages.SettingsMachinesNetworkOverlapsProxies.With("network", network.ToString(), "proxies", listed.ToString())));
            }
        }

        return problems;
    }
}
