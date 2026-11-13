// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;

namespace DDT.Core.Unattend;

// The run values a static network comes from, and their IPv4 forms. Lists are separated by semicolons.
public static class NetworkValues
{
    public const string Addresses = "IPAddress";
    public const string Gateway = "Gateway";
    public const string DnsServers = "DnsServers";

    public const int MaxListLength = 8;

    // 10.0.4.51/24, several joined by semicolons.
    public static IReadOnlyList<string>? ParseAddresses(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<string> addresses = [];

        foreach (string part in Parts(text))
        {
            int slash = part.IndexOf('/', StringComparison.Ordinal);

            if (slash < 0
                || ParseAddress(part[..slash]) is not { } address
                || !int.TryParse(part[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int prefix)
                || prefix is < 1 or > 32)
            {
                return null;
            }

            addresses.Add(string.Create(CultureInfo.InvariantCulture, $"{address}/{prefix}"));
        }

        return addresses.Count is > 0 and <= MaxListLength ? addresses : null;
    }

    public static IReadOnlyList<string>? ParseAddressList(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<string> addresses = [];

        foreach (string part in Parts(text))
        {
            if (ParseAddress(part) is not { } address)
            {
                return null;
            }

            addresses.Add(address);
        }

        return addresses.Count is > 0 and <= MaxListLength ? addresses : null;
    }

    // Four decimal numbers. IPAddress.TryParse would also take 10 or 0x0A000001.
    public static string? ParseAddress(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string[] octets = text.Trim().Split('.');

        if (octets.Length != 4)
        {
            return null;
        }

        byte[] bytes = new byte[4];

        for (int index = 0; index < 4; index++)
        {
            if (octets[index].Length is 0 or > 3
                || !byte.TryParse(octets[index], NumberStyles.None, CultureInfo.InvariantCulture, out bytes[index]))
            {
                return null;
            }
        }

        return string.Join('.', bytes.Select(octet => octet.ToString(CultureInfo.InvariantCulture)));
    }

    // 00155D010203 as Setup's Identifier wants it: 00-15-5D-01-02-03.
    public static string Identifier(string macAddress)
    {
        ArgumentNullException.ThrowIfNull(macAddress);

        string hex = macAddress.Replace(":", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);

        if (hex.Length != 12 || !hex.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException("A MAC address has 12 hexadecimal digits.", nameof(macAddress));
        }

        return string.Join('-', Enumerable.Range(0, 6).Select(index => hex.Substring(index * 2, 2).ToUpperInvariant()));
    }

    private static IEnumerable<string> Parts(string text) =>
        text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
