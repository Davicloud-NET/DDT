// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Settings;

internal static class SettingsCollections
{
    // Two keys that differ only in case are one key: the one read last wins, as configuration keys do.
    public static Dictionary<string, T> IgnoringCase<T>(IEnumerable<KeyValuePair<string, T>> entries)
    {
        Dictionary<string, T> result = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string key, T value) in entries)
        {
            result[key] = value;
        }

        return result;
    }
}
