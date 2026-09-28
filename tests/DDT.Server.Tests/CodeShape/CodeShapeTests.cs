// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Xunit;

namespace DDT.Server.Tests.CodeShape;

// Holds the code to the limits of docs/code-style.md. A finding outside the baseline fails, and so does a baseline
// entry that no longer applies, so the baseline only shrinks. Set DDT_CODE_SHAPE_WRITE=1 to rewrite it after a fix.
public sealed class CodeShapeTests
{
    private const string BaselineFile = "tests/code-shape-baseline.txt";

    [Fact]
    public void EveryFileKeepsTheLimitsOfTheCodeStyle()
    {
        string root = Repository.Root();
        string baselinePath = Path.Combine(root, BaselineFile);
        SortedSet<string> found = CodeShapeScanner.Scan(root);

        if (Environment.GetEnvironmentVariable("DDT_CODE_SHAPE_WRITE") == "1")
        {
            File.WriteAllText(baselinePath, string.Concat(found.Select(finding => finding + "\n")));
            return;
        }

        SortedSet<string> baseline = new(ReadBaseline(baselinePath), StringComparer.Ordinal);
        List<string> added = [.. found.Except(baseline)];
        List<string> gone = [.. baseline.Except(found)];

        Assert.True(added.Count == 0 && gone.Count == 0, Describe(added, gone));
    }

    private static IEnumerable<string> ReadBaseline(string path) =>
        File.Exists(path) ? File.ReadAllLines(path).Where(line => line.Length > 0) : [];

    private static string Describe(List<string> added, List<string> gone)
    {
        List<string> lines = [];

        if (added.Count > 0)
        {
            lines.Add($"Breaks docs/code-style.md ({added.Count}):");
            lines.AddRange(added.Select(finding => "  " + finding));
        }

        if (gone.Count > 0)
        {
            lines.Add($"Fixed, so remove from {BaselineFile} ({gone.Count}), or run with DDT_CODE_SHAPE_WRITE=1:");
            lines.AddRange(gone.Select(finding => "  " + finding));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
