// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Packages;

namespace DDT.Server.Packages;

// Keeps a Windows package's facts in the package row's columns.
public static class WindowsPackageColumns
{
    public static void Write(Package package, WindowsPackageFacts facts)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(facts);

        package.Content = facts.Content;
        package.Languages = Joined(facts.Languages, PackageLimits.MaxLanguagesLength);
        package.Architecture = facts.Architecture;
        package.KbNumbers = Joined(facts.KbNumbers.Select(kb => kb.ToString(CultureInfo.InvariantCulture)), PackageLimits.MaxKbNumbersLength);
    }

    public static WindowsPackageFacts? Read(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);

        if (package.Kind != PackageKind.WindowsPackage || package.Content is not { } content)
        {
            return null;
        }

        return new WindowsPackageFacts(
            content,
            Split(package.Languages),
            package.Architecture,
            [.. Split(package.KbNumbers).Select(kb => int.Parse(kb, NumberStyles.None, CultureInfo.InvariantCulture))]);
    }

    // As many values as fit, whole. Null for none.
    private static string? Joined(IEnumerable<string> values, int maxLength)
    {
        List<string> kept = [];
        int length = -1;

        foreach (string value in values)
        {
            if (length + 1 + value.Length > maxLength)
            {
                break;
            }

            kept.Add(value);
            length += 1 + value.Length;
        }

        return kept.Count == 0 ? null : string.Join(',', kept);
    }

    private static string[] Split(string? values) => values?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
}
