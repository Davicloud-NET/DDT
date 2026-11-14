// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;

namespace DDT.ConsoleProtocol;

// A step detail the console and the web put in their own words, written "#key" or "#key 5". Other text, such as an
// application's name, is shown as it is.
public static class StepDetails
{
    public const string DownloadingUpdates = "downloadingUpdates";

    public const string InstallingUpdates = "installingUpdates";

    public static string Coded(string key, int? number = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        return number is { } value ? $"#{key} {value.ToString(CultureInfo.InvariantCulture)}" : $"#{key}";
    }

    public static bool TryRead(string? detail, out string key, out int? number)
    {
        key = "";
        number = null;

        if (detail is not ['#', .. string rest])
        {
            return false;
        }

        int space = rest.IndexOf(' ', StringComparison.Ordinal);
        string name = space < 0 ? rest : rest[..space];

        if (name.Length == 0 || !name.All(char.IsAsciiLetterOrDigit))
        {
            return false;
        }

        if (space >= 0)
        {
            if (!int.TryParse(rest[(space + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                return false;
            }

            number = value;
        }

        key = name;

        return true;
    }
}
