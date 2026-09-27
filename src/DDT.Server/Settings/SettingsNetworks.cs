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

// The rules that compare networks: how wide one may be without a confirmation, and why a zero touch network must not
// hold a trusted proxy.
internal static class SettingsNetworks
{
    private const int WidestVersion4 = 16;
    private const int WidestVersion6 = 48;

    // Wide says what an address in the network counts as, with the network and the widest prefix it may have.
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

    // A request a proxy forwards without the client's address comes from the proxy's own, which a zero touch network
    // must not contain. Field names the zero touch field on a problem of the machines section; null puts the problem on
    // the proxy field of the proxies section.
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
