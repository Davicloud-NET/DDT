// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using WixToolset.Dtf.WindowsInstaller;

namespace DDT.Installer.CustomActions;

// Fills the netboot list with cards that have an IPv4 address. Picks the one with the default route.
public static class NetworkCards
{
    private const string Property = "NETBOOTINTERFACE";

    [CustomAction]
    public static ActionResult ListNetworkCards(Session session)
    {
        NetworkInterface[] cards = [.. NetworkInterface.GetAllNetworkInterfaces()
            .Where(card => card.OperationalStatus == OperationalStatus.Up
                && card.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                && Address(card) is not null)
            .OrderBy(card => card.Name, StringComparer.OrdinalIgnoreCase)];

        Dictionary<int, int> profiles = FirewallProfiles.OfCards();
        int allowed = FirewallProfiles.Allowed(session);

        using View view = session.Database.OpenView("SELECT * FROM `ComboBox`");
        view.Execute();

        // Order 1 is the ListItem for choosing later.
        int order = 2;

        foreach (NetworkInterface card in cards)
        {
            int? blocked = Blocked(card, profiles, allowed);

            using Record row = new(4);
            row[1] = Property;
            row[2] = order++;
            row[3] = card.Name;
            row[4] = blocked is { } profile
                ? $"{card.Name} ({Address(card)}), {FirewallProfiles.Name(profile)} network: firewall blocks DDT"
                : $"{card.Name} ({Address(card)})";
            view.InsertTemporary(row);
        }

        NetworkInterface? routed = cards.FirstOrDefault(HasDefaultRoute);

        if (string.IsNullOrEmpty(session[Property]) && routed is not null)
        {
            session[Property] = routed.Name;
        }

        // Shows ServerDlg's warning. Only for the main network: Hyper-V's switches are public too.
        session["BLOCKEDNETWORK"] = routed is not null && Blocked(routed, profiles, allowed) is not null ? "1" : string.Empty;

        return ActionResult.Success;
    }

    private static int? Blocked(NetworkInterface card, Dictionary<int, int> profiles, int allowed) =>
        card.GetIPProperties().GetIPv4Properties() is { } ipv4
        && profiles.TryGetValue(ipv4.Index, out int profile)
        && (allowed & profile) == 0
            ? profile
            : null;

    private static IPAddress? Address(NetworkInterface card) =>
        card.GetIPProperties().UnicastAddresses
            .Select(unicast => unicast.Address)
            .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);

    private static bool HasDefaultRoute(NetworkInterface card) =>
        card.GetIPProperties().GatewayAddresses
            .Any(gateway => gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any));
}
