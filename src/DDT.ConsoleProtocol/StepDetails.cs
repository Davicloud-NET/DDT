// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;

namespace DDT.ConsoleProtocol;

// A step detail the console and the web put in their own words, written "#key", "#key 5" or "#key some name". Other
// text, such as an application's name, is shown as it is.
public static class StepDetails
{
    public const string DownloadingUpdates = "downloadingUpdates";

    public const string InstallingUpdates = "installingUpdates";

    // With the name of the Windows package DISM adds.
    public const string AddingPackage = "addingPackage";

    public const string CleaningUp = "cleaningUp";

    // With the percent of the Windows volume BitLocker has encrypted.
    public const string Encrypting = "encrypting";

    public static string Coded(string key, int? number = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        return number is { } value ? $"#{key} {value.ToString(CultureInfo.InvariantCulture)}" : $"#{key}";
    }

    public static string Coded(string key, string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return $"#{key} {text}";
    }

    public static bool TryRead(string? detail, out string key, out int? number)
    {
        number = null;

        if (!TryReadText(detail, out key, out string? text))
        {
            return false;
        }

        if (text is not null)
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                key = "";

                return false;
            }

            number = value;
        }

        return true;
    }

    // The text after the key, or null when there is none.
    public static bool TryReadText(string? detail, out string key, out string? text)
    {
        key = "";
        text = null;

        if (detail is not ['#', .. string rest])
        {
            return false;
        }

        int space = rest.IndexOf(' ', StringComparison.Ordinal);
        string name = space < 0 ? rest : rest[..space];

        if (name.Length == 0 || !name.All(char.IsAsciiLetterOrDigit) || (space >= 0 && space == rest.Length - 1))
        {
            return false;
        }

        key = name;
        text = space < 0 ? null : rest[(space + 1)..];

        return true;
    }
}
