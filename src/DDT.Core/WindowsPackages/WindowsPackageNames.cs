// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text.RegularExpressions;

namespace DDT.Core.WindowsPackages;

// Reads what a .msu or .cab is from its name alone, as Microsoft names them on the update catalog and the Languages and
// Optional Features ISO. A .msu is a WIM and a .cab a cabinet, and the server reads neither.
public static partial class WindowsPackageNames
{
    private const string LanguageFeaturePrefix = "Microsoft-Windows-LanguageFeatures-";

    public static bool IsPackageFile(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return name.EndsWith(".msu", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".cab", StringComparison.OrdinalIgnoreCase);
    }

    // Null for a name that is no .msu or .cab.
    public static WindowsPackageFile? Read(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.EndsWith(".msu", StringComparison.OrdinalIgnoreCase))
        {
            return Update(name);
        }

        if (!name.EndsWith(".cab", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (LanguagePackPattern().Match(name) is { Success: true } pack)
        {
            return new(
                name,
                WindowsPackageFileKind.LanguagePack,
                Language(pack.Groups["language"].Value),
                Architecture(pack.Groups["architecture"].Value),
                null);
        }

        if (FeaturePattern().Match(name) is not { Success: true } feature)
        {
            return new(name, WindowsPackageFileKind.Feature, null, null, null);
        }

        string architecture = feature.Groups["architecture"].Value;

        // Language features name their language, not their culture field: ...-Basic-de-de-Package~...~amd64~~.cab.
        if (feature.Groups["name"].Value.StartsWith(LanguageFeaturePrefix, StringComparison.OrdinalIgnoreCase))
        {
            Match tagged = LanguageFeaturePattern().Match(feature.Groups["name"].Value[LanguageFeaturePrefix.Length..]);

            return new(
                name,
                WindowsPackageFileKind.LanguageFeature,
                tagged.Success ? Language(tagged.Groups["language"].Value) : null,
                Architecture(architecture),
                null);
        }

        return new(name, WindowsPackageFileKind.Feature, Language(feature.Groups["culture"].Value), Architecture(architecture), null);
    }

    // de-de becomes de-DE, sr-latn-rs sr-Latn-RS. Null for an empty tag.
    public static string? Language(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        if (tag.Length == 0)
        {
            return null;
        }

        string[] parts = tag.Split('-');

        for (int index = 0; index < parts.Length; index++)
        {
            string part = parts[index];
            parts[index] = index == 0 ? part.ToLowerInvariant()
                : part.Length == 4 ? char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()
                : part.ToUpperInvariant();
        }

        return string.Join('-', parts);
    }

    private static WindowsPackageFile Update(string name)
    {
        Match kb = KbPattern().Match(name);
        int? number = kb.Success && int.TryParse(kb.Groups["digits"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
        Match architecture = UpdateArchitecturePattern().Match(name);

        string? shown = architecture.Success ? Architecture(architecture.Groups["architecture"].Value) : null;

        return new(name, WindowsPackageFileKind.Update, null, shown, number);
    }

    // wow64 cabinets are the 32-bit parts of a feature for x64 Windows.
    private static string? Architecture(string value) => value.ToLowerInvariant() switch
    {
        "amd64" or "x64" or "wow64" => "x64",
        "arm64" => "arm64",
        "x86" => "x86",
        _ => null,
    };

    [GeneratedRegex(
        @"^Microsoft-Windows-(?:Client|Server|Lip)-Language-Pack_(?<architecture>[A-Za-z0-9]+)_(?<language>[A-Za-z0-9-]+)\.cab\z",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LanguagePackPattern();

    // <name>-Package~<public key token>~<architecture>~<culture>~<version>.cab, version empty on the ISO.
    [GeneratedRegex(
        @"^(?<name>.+?)-Package~[0-9a-f]{16}~(?<architecture>[A-Za-z0-9.]*)~(?<culture>[A-Za-z0-9-]*)~[0-9.]*\.cab\z",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex FeaturePattern();

    [GeneratedRegex(@"^[A-Za-z]+-(?<language>[a-z]{2,3}(?:-[a-z0-9]{2,8})+)\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LanguageFeaturePattern();

    [GeneratedRegex(@"kb(?<digits>[0-9]{4,9})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex KbPattern();

    [GeneratedRegex(@"-(?<architecture>arm64|x64|x86)(?![A-Za-z0-9])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex UpdateArchitecturePattern();
}
