// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Applications.Installers;

// Recognises an uploaded installer, an MSI or an EXE, by its content. A file it can't read is Unknown, never an error.
public static class InstallerProbe
{
    // Installer stubs carry an icon and a manifest; their resources are a few hundred kilobytes.
    private const int MaxResourceLength = 8 * 1024 * 1024;

    public static InstallerFacts Read(Stream installer)
    {
        ArgumentNullException.ThrowIfNull(installer);

        if (!installer.CanRead || !installer.CanSeek)
        {
            throw new ArgumentException("The stream must be readable and seekable.", nameof(installer));
        }

        try
        {
            return CompoundFileHeader.HasSignature(installer.ReadAtMost(0, CompoundFileHeader.Length)) ? ReadMsi(installer) : ReadExecutable(installer);
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
        {
            return InstallerFacts.Unknown;
        }
    }

    private static InstallerFacts ReadMsi(Stream installer) =>
        MsiProperties.Read(installer) is { } msi
            ? new InstallerFacts(InstallerKind.Msi, msi.ProductName, msi.Manufacturer, msi.ProductVersion, msi.ProductCode, msi.UpgradeCode)
            : InstallerFacts.Unknown;

    private static InstallerFacts ReadExecutable(Stream installer)
    {
        if (PeLayout.Read(installer) is not { } layout)
        {
            return InstallerFacts.Unknown;
        }

        PeSection? resourceSection = layout.ResourceRva == 0 ? null : layout.SectionAt(layout.ResourceRva);
        byte[] resources = resourceSection is null
            ? []
            : installer.ReadAtMost(resourceSection.RawOffset, (int)Math.Min(resourceSection.RawLength, MaxResourceLength));
        PeVersionInfo version = resourceSection is null ? PeVersionInfo.None : PeVersionResource.Read(resources, resourceSection, layout.ResourceRva);
        (InstallerKind kind, string? bundleCode) = ExecutableInstallerMarkers.Recognize(installer, layout, resources);
        string? uninstallKey = UninstallKeyOf(installer, kind, layout, resources, resourceSection);

        return new InstallerFacts(kind, version.ProductName, version.CompanyName, version.ProductVersion, BundleCode: bundleCode, UninstallKey: uninstallKey);
    }

    // The file comes from outside and the readers decompress parts of it, so any failure there is no key, never an
    // error for the upload.
    private static string? UninstallKeyOf(Stream installer, InstallerKind kind, PeLayout layout, ReadOnlySpan<byte> resources, PeSection? resourceSection)
    {
        try
        {
            return kind switch
            {
                InstallerKind.InnoSetup when resourceSection is not null => InnoSetupKey.Read(installer, resources, resourceSection, layout.ResourceRva),
                _ => null,
            };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }
}
