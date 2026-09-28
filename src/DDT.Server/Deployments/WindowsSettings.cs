// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Core.Unattend;

namespace DDT.Server.Deployments;

// Whether a setting the answer file takes is one Windows knows. The server checks each wherever it comes from, since the
// agent runs without globalization data.
public static class WindowsSettings
{
    public static bool IsTimeZone(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return WindowsTimeZones.IsValidId(value.Trim());
    }

    // A neutral culture such as de names no region, and Windows needs one for its locales.
    public static bool IsLocale(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        try
        {
            return !CultureInfo.GetCultureInfo(value.Trim(), predefinedOnly: true).IsNeutralCulture;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    public static bool IsKeyboard(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.Split(';').All(IsInputLocale);
    }

    // A culture name, a language and keyboard layout pair such as 0407:00000407, or a language and a text service
    // written as two GUIDs, as Windows lists them.
    private static bool IsInputLocale(string part)
    {
        string value = part.Trim();

        if (value.Length > 5 && value[4] == ':' && value[..4].All(char.IsAsciiHexDigit))
        {
            string layout = value[5..];

            return (layout.Length == 8 && layout.All(char.IsAsciiHexDigit))
                || (layout.Length == 76
                    && Guid.TryParseExact(layout[..38], "B", out _)
                    && Guid.TryParseExact(layout[38..], "B", out _));
        }

        return value.Length > 0 && IsLocale(value);
    }
}
