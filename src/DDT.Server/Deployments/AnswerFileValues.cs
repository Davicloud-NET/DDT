// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Unattend;

namespace DDT.Server.Deployments;

// The product key and the static network a run's values give its answer file, checked as the run starts and again
// when the file is made. A problem names where a key came from, never the key.
public static class AnswerFileValues
{
    public static (ProductKey? Key, string? Problem) ProductKeyOf(Func<string, string?> values, string? configured)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (Value(values(WindowsSetupValues.ProductKeyValue)) is { } given)
        {
            return ProductKey.IsValid(given)
                ? (new ProductKey(given.ToUpperInvariant()), null)
                : (null, $"The run's {WindowsSetupValues.ProductKeyValue} value is not five groups of five letters or digits.");
        }

        if (Value(configured) is not { } key)
        {
            return (null, null);
        }

        return ProductKey.IsValid(key)
            ? (new ProductKey(key.ToUpperInvariant()), null)
            : (null, "The product key on the Deployment defaults page is not five groups of five letters or digits.");
    }

    // No IPAddress is DHCP, whatever else is set.
    public static (StaticNetwork? Network, string? Problem) NetworkOf(Func<string, string?> values, string macAddress)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(macAddress);

        if (Value(values(NetworkValues.Addresses)) is not { } addressText)
        {
            return (null, null);
        }

        if (NetworkValues.ParseAddresses(addressText) is not { } addresses)
        {
            return (null, $"The run's {NetworkValues.Addresses} value, {addressText}, is not a list of up to {NetworkValues.MaxListLength} IPv4 addresses with a prefix length, such as 10.0.4.51/24.");
        }

        string? gatewayText = Value(values(NetworkValues.Gateway));
        string? gateway = gatewayText is null ? null : NetworkValues.ParseAddress(gatewayText);

        if (gatewayText is not null && gateway is null)
        {
            return (null, $"The run's {NetworkValues.Gateway} value, {gatewayText}, is not an IPv4 address.");
        }

        string? dnsText = Value(values(NetworkValues.DnsServers));
        IReadOnlyList<string>? dnsServers = dnsText is null ? [] : NetworkValues.ParseAddressList(dnsText);

        if (dnsServers is null)
        {
            return (null, $"The run's {NetworkValues.DnsServers} value, {dnsText}, is not a list of up to {NetworkValues.MaxListLength} IPv4 addresses.");
        }

        return (new StaticNetwork(NetworkValues.Identifier(macAddress), addresses, gateway, dnsServers), null);
    }

    private static string? Value(string? setting) => string.IsNullOrWhiteSpace(setting) ? null : setting.Trim();
}
