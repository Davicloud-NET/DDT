// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;

namespace DDT.Agent.Applications;

public static class FileVersions
{
    // Compares part by part as numbers, so 1.10 is after 1.9. A missing part counts as 0, and so does one that is no
    // number.
    public static bool IsAtLeast(string version, string minimum)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(minimum);

        string[] have = version.Trim().Split('.');
        string[] need = minimum.Trim().Split('.');

        for (int index = 0; index < Math.Max(have.Length, need.Length); index++)
        {
            int compared = Part(have, index).CompareTo(Part(need, index));

            if (compared != 0)
            {
                return compared > 0;
            }
        }

        return true;
    }

    private static long Part(string[] parts, int index) =>
        index < parts.Length && long.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out long value) ? value : 0;
}
