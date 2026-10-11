// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text.RegularExpressions;

namespace DDT.Agent.Deployment;

// What Linux has mounted below a folder. Deleting the folder with all it holds would delete the files of what a
// script left mounted there, such as a disk or a share.
internal static partial class MountsBelow
{
    private const string List = "/proc/self/mounts";

    // Empty on a system that mounts nothing into folders this way, and where the kernel's list cannot be read.
    public static IReadOnlyList<string> Of(string directory)
    {
        if (!OperatingSystem.IsLinux())
        {
            return [];
        }

        try
        {
            return In(File.ReadAllLines(List), directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // lines are the kernel's: device, place, file system and options, with a space between.
    public static IReadOnlyList<string> In(IEnumerable<string> lines, string directory)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentException.ThrowIfNullOrEmpty(directory);

        string below = directory.TrimEnd('/') + "/";

        return [.. lines
            .Select(line => line.Split(' '))
            .Where(fields => fields.Length >= 2)
            .Select(fields => Unescape(fields[1]))
            .Where(place => place.StartsWith(below, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)];
    }

    // The list writes a space, a tab, a line end and a backslash as a backslash and three octal digits.
    private static string Unescape(string field) =>
        Escape().Replace(field, match => ((char)Convert.ToInt32(match.Groups[1].Value, 8)).ToString(CultureInfo.InvariantCulture));

    [GeneratedRegex(@"\\([0-7]{3})")]
    private static partial Regex Escape();
}
