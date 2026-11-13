// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.RegularExpressions;

namespace DDT.Agent.Applications;

// winget's output without its spinner, progress bars and terminal escapes, which it writes into a pipe too.
public static partial class WingetOutput
{
    // The line for the log, or null for one that only drew progress.
    public static string? Line(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        string text = Escape().Replace(line, string.Empty).Trim();

        return text.Length == 0
            || text.Contains('█', StringComparison.Ordinal)
            || text.Contains('▒', StringComparison.Ordinal)
            || Progress().IsMatch(text)
            ? null
            : text;
    }

    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]", RegexOptions.CultureInvariant)]
    private static partial Regex Escape();

    // A spinner's frame, a percentage, or a size such as 1.50 MB / 3.00 MB.
    [GeneratedRegex(@"^(?:[-\\|/]|\d+\s*%|[\d.,]+\s*[KMG]?B\s*/\s*[\d.,]+\s*[KMG]?B)$", RegexOptions.CultureInvariant)]
    private static partial Regex Progress();
}
