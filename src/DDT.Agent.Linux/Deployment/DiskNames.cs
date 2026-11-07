// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text.RegularExpressions;

namespace DDT.Agent.Deployment;

// Which entries of /sys/block are disks DDT looks at, and the order they are numbered in.
public sealed partial class DiskNames : IComparer<string>
{
    public static DiskNames Order { get; } = new();

    // SCSI, SATA and USB disks, NVMe namespaces, SD and eMMC cards, and the disks of virtual machines. Not the boot
    // areas of an eMMC, the hidden paths of a multipath NVMe, nor loop, RAM, optical and mapped devices.
    public static bool IsDisk(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return Disk().IsMatch(name);
    }

    // The kinds one after the other, and within a kind as a person counts: sdz before sdaa, nvme2n1 before nvme10n1.
    public int Compare(string? x, string? y)
    {
        Match left = Disk().Match(x ?? "");
        Match right = Disk().Match(y ?? "");
        int kinds = string.CompareOrdinal(left.Groups["kind"].Value, right.Groups["kind"].Value);

        if (kinds != 0 || !left.Success || !right.Success)
        {
            return kinds != 0 ? kinds : string.CompareOrdinal(x, y);
        }

        MatchCollection leftRuns = Run().Matches(left.Groups["rest"].Value);
        MatchCollection rightRuns = Run().Matches(right.Groups["rest"].Value);

        for (int index = 0; index < Math.Min(leftRuns.Count, rightRuns.Count); index++)
        {
            int compared = CompareRuns(leftRuns[index].Value, rightRuns[index].Value);

            if (compared != 0)
            {
                return compared;
            }
        }

        return leftRuns.Count.CompareTo(rightRuns.Count);
    }

    // Numbers by value, letters by length and then by alphabet.
    private static int CompareRuns(string a, string b)
    {
        if (char.IsAsciiDigit(a[0]) && char.IsAsciiDigit(b[0]))
        {
            return Number(a).CompareTo(Number(b));
        }

        return a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
    }

    private static ulong Number(string digits) =>
        ulong.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out ulong number) ? number : ulong.MaxValue;

    [GeneratedRegex(@"^(?:(?<kind>sd|vd|xvd)(?<rest>[a-z]+)|(?<kind>nvme)(?<rest>\d+n\d+)|(?<kind>mmcblk)(?<rest>\d+))$", RegexOptions.ExplicitCapture)]
    private static partial Regex Disk();

    [GeneratedRegex(@"\d+|\D+")]
    private static partial Regex Run();
}
