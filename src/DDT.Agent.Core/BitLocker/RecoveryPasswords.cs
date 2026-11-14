// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.RegularExpressions;

namespace DDT.Agent.BitLocker;

// Finds recovery passwords and protector ids in manage-bde's output by their shape, which holds in every display
// language: 48 digits in 8 groups of 6, and a GUID in braces.
public static partial class RecoveryPasswords
{
    public static IReadOnlyList<string> Find(IEnumerable<string> lines) =>
        [.. lines.SelectMany(line => PasswordPattern().Matches(line).Select(match => match.Value)).Distinct(StringComparer.Ordinal)];

    public static IReadOnlyList<Guid> ProtectorIds(IEnumerable<string> lines) =>
        [.. lines.SelectMany(line => ProtectorIdPattern().Matches(line).Select(match => Guid.Parse(match.Groups[1].Value))).Distinct()];

    // A line for the log, or null for one that holds a password.
    public static string? Redacted(string line) => PasswordPattern().IsMatch(line) ? null : line;

    [GeneratedRegex(@"(?<!\d)\d{6}(?:-\d{6}){7}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PasswordPattern();

    [GeneratedRegex(@"\{([0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})\}", RegexOptions.CultureInvariant)]
    private static partial Regex ProtectorIdPattern();
}
