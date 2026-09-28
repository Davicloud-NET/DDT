// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Xunit;

namespace DDT.Server.Tests.CodeShape;

// Holds the code to the limits of docs/code-style.md that no analyzer checks.
public sealed class CodeShapeTests
{
    // Each one argued where it is declared.
    private static readonly HashSet<string> s_exceptions = new(StringComparer.Ordinal)
    {
        // Name and value pairs read like the message's text; three pairs are six parameters.
        "src/DDT.Contracts/Messages/MessageTemplate.cs: MessageTemplate.With: over 5 parameters",
    };

    [Fact]
    public void EveryFileKeepsTheLimitsOfTheCodeStyle()
    {
        SortedSet<string> found = CodeShapeScanner.Scan(Repository.Root());
        List<string> breaking = [.. found.Except(s_exceptions)];
        List<string> stale = [.. s_exceptions.Except(found)];

        Assert.True(breaking.Count == 0 && stale.Count == 0, Describe(breaking, stale));
    }

    private static string Describe(List<string> breaking, List<string> stale)
    {
        List<string> lines = [];

        if (breaking.Count > 0)
        {
            lines.Add($"Breaks docs/code-style.md ({breaking.Count}):");
            lines.AddRange(breaking.Select(finding => "  " + finding));
        }

        if (stale.Count > 0)
        {
            lines.Add($"No longer needed in s_exceptions ({stale.Count}):");
            lines.AddRange(stale.Select(finding => "  " + finding));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
