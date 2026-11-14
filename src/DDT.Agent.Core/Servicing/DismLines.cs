// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text.RegularExpressions;

namespace DDT.Agent.Servicing;

// Reads one DISM call's output for the log: the image's version it prints first, and the percent of its progress bar.
public sealed partial class DismLines(IProgress<int>? percent)
{
    public Version? ImageVersion { get; private set; }

    public static Version? VersionIn(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        return lines.Select(VersionOf).FirstOrDefault(version => version is not null);
    }

    // DISM draws its progress bar on one line again and again. The bar only gives the percent, and the log gets the rest.
    public string? Read(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (Bar().Match(line) is not { Success: true } bar)
        {
            ImageVersion ??= VersionOf(line);

            return line;
        }

        percent?.Report((int)double.Parse(bar.Groups["percent"].Value, CultureInfo.InvariantCulture));

        return null;
    }

    private static Version? VersionOf(string line) =>
        VersionLine().Match(line) is { Success: true } match && Version.TryParse(match.Groups["version"].Value, out Version? version) ? version : null;

    [GeneratedRegex(@"^\[[=\s]*(?<percent>\d{1,3}\.\d)%[=\s]*\]$", RegexOptions.CultureInvariant)]
    private static partial Regex Bar();

    [GeneratedRegex(@"^Image Version: (?<version>\d+\.\d+\.\d+\.\d+)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionLine();
}
