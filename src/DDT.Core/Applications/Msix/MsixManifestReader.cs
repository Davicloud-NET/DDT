// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DDT.Core.Applications.Msix;

// Reads the manifest of an .msix or .appx (AppxManifest.xml) or of a bundle (AppxMetadata/AppxBundleManifest.xml).
// The file may come from anyone, so whatever is wrong with it makes it unreadable, never an exception.
public static partial class MsixManifestReader
{
    public const string PackageManifest = "AppxManifest.xml";
    public const string BundleManifest = "AppxMetadata/AppxBundleManifest.xml";

    // The manifest schema's limit on a publisher, and more entries than any real package has.
    private const int MaxPublisherLength = 8192;
    private const ulong MaxEntries = 200_000;

    // A bundle's application package is read in memory for its names. A larger one keeps the identity's.
    private const long MaxInnerPackageBytes = 256L * 1024 * 1024;

    private const string ResourcePrefix = "ms-resource:";

    // Null when it is no package or bundle that can be read. The stream must be seekable.
    public static MsixManifest? Read(Stream package)
    {
        ArgumentNullException.ThrowIfNull(package);

        try
        {
            if (ZipEntryCount.Of(package) is not { } count || count > MaxEntries)
            {
                return null;
            }

            package.Position = 0;
            using ZipArchive archive = new(package, ZipArchiveMode.Read, leaveOpen: true);
            ZipArchiveEntry? bundle = Find(archive, BundleManifest);

            if ((bundle ?? Find(archive, PackageManifest)) is not { } manifest || manifest.Length > MsixXml.MaxBytes)
            {
                return null;
            }

            using Stream data = manifest.Open();

            if (MsixXml.Root(data) is not { } root)
            {
                return null;
            }

            return FromRoot(root, bundle is null ? MsixXml.Child(root, "Properties") : MainProperties(archive, root), isBundle: bundle is not null);
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or NotSupportedException)
        {
            return null;
        }
    }

    private static ZipArchiveEntry? Find(ZipArchive archive, string name) =>
        archive.Entries.FirstOrDefault(entry => string.Equals(entry.FullName.Replace('\\', '/'), name, StringComparison.OrdinalIgnoreCase));

    // A bundle names nothing itself, so its first application package inside it does.
    private static XElement? MainProperties(ZipArchive archive, XElement bundle)
    {
        XElement? main = MsixXml.Child(bundle, "Packages")?.Elements()
            .FirstOrDefault(package => package.Name.LocalName == "Package"
                && string.Equals((string?)package.Attribute("Type"), "application", StringComparison.OrdinalIgnoreCase));

        if ((string?)main?.Attribute("FileName") is not { } fileName || Find(archive, fileName) is not { Length: <= MaxInnerPackageBytes } entry)
        {
            return null;
        }

        try
        {
            using MemoryStream copy = new();

            using (Stream data = entry.Open())
            {
                if (!CopyAtMost(data, copy, MaxInnerPackageBytes))
                {
                    return null;
                }
            }

            copy.Position = 0;
            using ZipArchive inner = new(copy, ZipArchiveMode.Read);

            if (Find(inner, PackageManifest) is not { Length: <= MsixXml.MaxBytes } manifest)
            {
                return null;
            }

            using Stream text = manifest.Open();

            return MsixXml.Child(MsixXml.Root(text), "Properties");
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    // An entry's length may lie, so the copy stops past max.
    private static bool CopyAtMost(Stream data, MemoryStream copy, long max)
    {
        byte[] buffer = new byte[81920];
        int count;

        while ((count = data.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (copy.Length + count > max)
            {
                return false;
            }

            copy.Write(buffer, 0, count);
        }

        return true;
    }

    private static MsixManifest? FromRoot(XElement root, XElement? properties, bool isBundle)
    {
        if (root.Name.LocalName != (isBundle ? "Bundle" : "Package") || MsixXml.Child(root, "Identity") is not { } identity)
        {
            return null;
        }

        string? name = (string?)identity.Attribute("Name");
        string? publisher = (string?)identity.Attribute("Publisher");
        string? version = (string?)identity.Attribute("Version");

        if (!PackageFamilyNames.IsIdentityName(name) || publisher is not { Length: > 0 and <= MaxPublisherLength } || !IsVersion(version))
        {
            return null;
        }

        return new MsixManifest(
            name,
            publisher,
            version,
            (string?)identity.Attribute("ProcessorArchitecture"),
            Shown(MsixXml.Child(properties, "DisplayName")) ?? name,
            Shown(MsixXml.Child(properties, "PublisherDisplayName")) ?? CommonName(publisher),
            !isBundle && string.Equals(MsixXml.Child(properties, "Framework")?.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase),
            isBundle);
    }

    // CN=Microsoft Corporation, O=… gives Microsoft Corporation. A quoted value may hold a comma.
    private static string? CommonName(string publisher) =>
        CommonNamePart().Match(publisher) is { Success: true } match
            ? match.Groups["name"].Value.Replace("\"\"", "\"", StringComparison.Ordinal).Trim() is { Length: > 0 } name ? name : null
            : null;

    // A reference into the package's resources names no text a person can read.
    private static string? Shown(XElement? element) =>
        element?.Value.Trim() is { Length: > 0 } text && !text.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase) ? text : null;

    [GeneratedRegex(@"(?:^|,)\s*CN\s*=\s*(?:""(?<name>(?:[^""]|"""")*)""|(?<name>[^,]*))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CommonNamePart();

    // Four numbers of 16 bits.
    private static bool IsVersion([NotNullWhen(true)] string? value) =>
        value?.Split('.') is { Length: 4 } parts
        && parts.All(part => ushort.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _));
}
