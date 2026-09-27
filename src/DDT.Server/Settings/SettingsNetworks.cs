// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
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

    public static IReadOnlyList<SettingWarning> Wide(IEnumerable<IPNetwork> networks, string field, string use) =>
    [
        .. networks
            .Where(network => network.PrefixLength < (network.BaseAddress.AddressFamily == AddressFamily.InterNetwork ? WidestVersion4 : WidestVersion6))
            .Select(network => new SettingWarning(
                field,
                $"{network} is wider than a /{(network.BaseAddress.AddressFamily == AddressFamily.InterNetwork ? WidestVersion4 : WidestVersion6)}. " +
                $"Every address in it counts as a {use} address.",
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
                    $"The zero touch network {network} contains the proxy {proxy}. A request the proxy forwards without the " +
                    "client's address would count as one from that network."));
            }
        }

        foreach (IPNetwork listed in DdtForwardedHeadersExtensions.Networks(proxies.KnownNetworks))
        {
            foreach (IPNetwork network in zeroTouch.Where(network => network.Contains(listed.BaseAddress) || listed.Contains(network.BaseAddress)))
            {
                problems.Add(new(
                    field ?? "KnownNetworks",
                    $"The zero touch network {network} overlaps the proxy network {listed}. A request a proxy there forwards " +
                    "without the client's address would count as one from that network."));
            }
        }

        return problems;
    }
}
