// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text.RegularExpressions;

namespace DDT.Agent.Deployment;

// The kernel's list of what is mounted, as /proc/self/mounts has it: a line each, its fields with a space between.
internal static partial class MountList
{
    // The list writes a space, a tab, a line end and a backslash as a backslash and three octal digits.
    public static string Unescape(string field) =>
        Escape().Replace(field, match => ((char)Convert.ToInt32(match.Groups[1].Value, 8)).ToString(CultureInfo.InvariantCulture));

    [GeneratedRegex(@"\\([0-7]{3})")]
    private static partial Regex Escape();
}
