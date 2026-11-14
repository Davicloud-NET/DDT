// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.RegularExpressions;

namespace DDT.Core.Sequences;

// Knowledge base numbers as Windows Update names them: KBArticleIDs holds the digits alone.
public static partial class KbNumbers
{
    // The digits of "KB5031234", "kb5031234" or "5031234", or null for anything else.
    public static string? Normalize(string? value) =>
        value is not null && Kb().Match(value.Trim()) is { Success: true } match ? match.Groups["digits"].Value : null;

    [GeneratedRegex(@"^(?:[Kk][Bb])?(?<digits>[0-9]{4,8})\z", RegexOptions.CultureInvariant)]
    private static partial Regex Kb();
}
