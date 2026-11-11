// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text.RegularExpressions;
using DDT.Contracts.Applications;
using DDT.Contracts.Messages;
using DDT.Core.Applications.Msix;

namespace DDT.Core.Applications;

// The rules of a detection rule. The request comes from outside, so members declared non-null can still be null.
public static partial class ApplicationDetectionRules
{
    private const string ProductCodeExample = "{23170F69-40C1-2702-2501-000001000000}";
    private const string PathExample = @"%ProgramFiles%\App\app.exe";

    // A file version has four parts of 16 bits.
    private const int MaxVersionPart = ushort.MaxValue;

    public static void Check(ApplicationDetection? detection, Action<string, ServerMessage> add)
    {
        ArgumentNullException.ThrowIfNull(add);

        switch (detection)
        {
            case ProductCodeDetection product when NormalizedProductCode(product.ProductCode) is null:
                add("detection.productCode", ServerMessages.ApplicationProductCode.With("example", ProductCodeExample));
                break;
            case UninstallKeyDetection key when !IsUninstallKey(key.KeyName):
                add("detection.keyName", ServerMessages.ApplicationUninstallKey.With("max", ApplicationLimits.MaxUninstallKeyLength));
                break;
            case FileDetection file:
                CheckFile(file, add);
                break;
            case PackageFamilyDetection family when !PackageFamilyNames.IsFamilyName(family.FamilyName?.Trim()):
                add("detection.familyName", ServerMessages.ApplicationPackageFamily.With("example", PackageFamilyNames.Example));
                break;
        }
    }

    // The product code in the form Windows writes it, {GUID} in upper case, or null if the value is no GUID.
    public static string? NormalizedProductCode(string? value)
    {
        string trimmed = value?.Trim() ?? "";

        return Guid.TryParseExact(trimmed, "B", out Guid code) || Guid.TryParseExact(trimmed, "D", out code)
            ? code.ToString("B").ToUpperInvariant()
            : null;
    }

    public static bool IsUninstallKey(string? value) =>
        value is not null
        && value.Trim() is { Length: > 0 and <= ApplicationLimits.MaxUninstallKeyLength } key
        && !key.Any(character => character is '\\' or '/' || char.IsControl(character));

    // A full local path, from a drive or one leading variable, to a file: no share, no empty, . or .. folder, and no
    // character Windows refuses in a name.
    public static bool IsFilePath(string? value)
    {
        string path = value?.Trim() ?? "";

        if (path.Length > ApplicationLimits.MaxDetectionPathLength || FilePath().Match(path) is not { Success: true } match)
        {
            return false;
        }

        string[] names = match.Groups["rest"].Value.Split('\\');

        return names.All(name => name.Length > 0 && name.Trim('.').Length > 0 && !name.Any(char.IsControl));
    }

    public static bool IsVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return VersionForm().IsMatch(value.Trim())
            && value.Trim().Split('.').All(part => int.Parse(part, NumberStyles.None, CultureInfo.InvariantCulture) <= MaxVersionPart);
    }

    private static void CheckFile(FileDetection file, Action<string, ServerMessage> add)
    {
        if (!IsFilePath(file.Path))
        {
            add(
                "detection.path",
                ServerMessages.ApplicationDetectionPath.With("example", PathExample, "max", ApplicationLimits.MaxDetectionPathLength));
        }

        if (!IsVersion(file.MinimumVersion))
        {
            add("detection.minimumVersion", ServerMessages.ApplicationDetectionVersion.With());
        }
    }

    // A drive or a %Variable%, then a backslash and the rest without : or another %.
    [GeneratedRegex(@"^(?:[A-Za-z]:|%[A-Za-z_][A-Za-z0-9_()]*%)\\(?<rest>[^:%<>""|?*/]+)\z", RegexOptions.CultureInvariant)]
    private static partial Regex FilePath();

    [GeneratedRegex(@"^[0-9]{1,5}(?:\.[0-9]{1,5}){0,3}\z", RegexOptions.CultureInvariant)]
    private static partial Regex VersionForm();
}
