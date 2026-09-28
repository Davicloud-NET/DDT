// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;

namespace DDT.Core.Sequences;

// IPv4 addresses as conditions write them: four decimal numbers of 0 to 255, nothing shorter. IPAddress.Parse would
// also take 10.1 or 0x0A000001, which a person reading the condition would not expect to match.
public static class Ipv4
{
    public static bool TryParse(string? text, out uint address)
    {
        address = 0;
        string[] parts = (text ?? "").Trim().Split('.');

        if (parts.Length != 4)
        {
            return false;
        }

        foreach (string part in parts)
        {
            if (part.Length is 0 or > 3
                || !part.All(char.IsAsciiDigit)
                || !byte.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out byte value))
            {
                address = 0;

                return false;
            }

            address = (address << 8) | value;
        }

        return true;
    }

    // A network written as 10.0.0.0/24; the address need not be the network's first.
    public static bool TryParseNetwork(string? text, out uint network, out uint mask)
    {
        network = 0;
        mask = 0;
        string[] parts = (text ?? "").Trim().Split('/');

        if (parts.Length != 2
            || !TryParse(parts[0], out uint address)
            || parts[1].Length is 0 or > 2
            || !parts[1].All(char.IsAsciiDigit)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int prefix)
            || prefix > 32)
        {
            return false;
        }

        mask = Mask(prefix);
        network = address & mask;

        return true;
    }

    public static uint Mask(int prefix) => prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);

    public static string Format(uint address) =>
        string.Create(CultureInfo.InvariantCulture, $"{address >> 24}.{(address >> 16) & 0xFF}.{(address >> 8) & 0xFF}.{address & 0xFF}");
}
