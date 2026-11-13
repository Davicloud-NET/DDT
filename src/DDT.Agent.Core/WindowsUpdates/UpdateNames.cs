// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;

namespace DDT.Agent.WindowsUpdates;

// How the log and the result name an update: its title, and the KB numbers the title does not already name.
public static class UpdateNames
{
    public static string Of(AvailableUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        string[] missing = [.. update.Kbs.Select(kb => "KB" + kb).Where(kb => !update.Title.Contains(kb, StringComparison.OrdinalIgnoreCase))];

        return missing.Length == 0 ? update.Title : $"{update.Title} ({string.Join(", ", missing)})";
    }

    public static string HResult(int hResult) => "0x" + hResult.ToString("X8", CultureInfo.InvariantCulture);
}
