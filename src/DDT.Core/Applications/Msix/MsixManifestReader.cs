// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace DDT.Core.Applications.Msix;

// Reads the manifest of an .msix or .appx (AppxManifest.xml) or of a bundle (AppxMetadata/AppxBundleManifest.xml).
// The file may come from anyone, so whatever is wrong with it makes it unreadable, never an exception.
public static class MsixManifestReader
{
    public const string PackageManifest = "AppxManifest.xml";
    public const string BundleManifest = "AppxMetadata/AppxBundleManifest.xml";

    // The manifest schema's limit on a publisher, and more entries than any real package has.
    private const int MaxPublisherLength = 8192;
    private const ulong MaxEntries = 200_000;

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

            return MsixXml.Root(data) is { } root ? FromRoot(root, isBundle: bundle is not null) : null;
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or NotSupportedException)
        {
            return null;
        }
    }

    private static ZipArchiveEntry? Find(ZipArchive archive, string name) =>
        archive.Entries.FirstOrDefault(entry => string.Equals(entry.FullName.Replace('\\', '/'), name, StringComparison.OrdinalIgnoreCase));

    private static MsixManifest? FromRoot(XElement root, bool isBundle)
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

        XElement? properties = isBundle ? null : MsixXml.Child(root, "Properties");

        return new MsixManifest(
            name,
            publisher,
            version,
            (string?)identity.Attribute("ProcessorArchitecture"),
            Shown(MsixXml.Child(properties, "DisplayName")) ?? name,
            Shown(MsixXml.Child(properties, "PublisherDisplayName")),
            string.Equals(MsixXml.Child(properties, "Framework")?.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase),
            isBundle);
    }

    // A reference into the package's resources names no text a person can read.
    private static string? Shown(XElement? element) =>
        element?.Value.Trim() is { Length: > 0 } text && !text.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase) ? text : null;

    // Four numbers of 16 bits.
    private static bool IsVersion([NotNullWhen(true)] string? value) =>
        value?.Split('.') is { Length: 4 } parts
        && parts.All(part => ushort.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _));
}
