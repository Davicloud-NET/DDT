// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using DDT.Contracts.Applications;
using DDT.Core.Applications;
using DDT.Core.Applications.Msix;
using DDT.Server.Applications.Installers;
using DDT.Server.Images;
using DDT.Server.Packages;

namespace DDT.Server.Applications;

// An application's files arrive as a zip, or as one file of any kind that the server zips. A .msix is a zip too, so the
// extension decides, not the content: it is zipped like any other file.
internal static class ApplicationUploads
{
    public static ApplicationFiles Files(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);

        return new ApplicationFiles(package.Id, package.OriginalFileName ?? package.Name, package.SizeBytes);
    }

    // The zip to store: the part file itself, or a zip of it written next to it, where the sweeper finds what a stop
    // left.
    public static async Task<string> SourceAsync(ImageStore store, ImageUpload upload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(upload);

        string part = store.PartPath(upload.Id);

        if (IsZip(upload.FileName))
        {
            return part;
        }

        string wrapped = store.RawPath(upload.Id);
        await SingleFileZip.WriteAsync(part, wrapped, EntryName(upload.FileName), cancellationToken).ConfigureAwait(false);

        return wrapped;
    }

    // Reads the uploaded installer or MSIX package, a zip's MSIX packages, or else the one .msi or .exe at a zip's root.
    // Call after the zip passed its check.
    public static async Task<ApplicationSuggestion?> SuggestAsync(ImageStore store, ImageUpload upload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(upload);

        string part = store.PartPath(upload.Id);
        string name = EntryName(upload.FileName);

        if (!IsZip(upload.FileName))
        {
            return MsixLayouts.IsPackage(name) ? Suggest(MsixLayouts.Of([name], _ => ManifestOf(part), _ => false)) : Suggest(part, name);
        }

        MsixLayoutResult msix;

        using (FileStream zip = new(part, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            msix = MsixZipLayout.Read(zip, store.UploadsDirectory);
        }

        // Packages with a problem suggest nothing: the problem shows when the application is saved.
        if (msix.HasPackages)
        {
            return Suggest(msix);
        }

        using ZipArchive archive = ZipFile.OpenRead(part);

        if (archive.Entries.Where(IsRootInstaller).ToArray() is not [ZipArchiveEntry installer])
        {
            return null;
        }

        // The probe seeks, and an entry's stream does not, so the installer is copied out first.
        string copy = store.RawPath(upload.Id);

        try
        {
            await using (Stream data = installer.Open())
            await using (FileStream file = new(copy, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
            {
                await data.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            return Suggest(copy, installer.FullName);
        }
        finally
        {
            File.Delete(copy);
        }
    }

    private static ApplicationSuggestion? Suggest(string path, string fileName)
    {
        InstallerFacts facts;

        using (FileStream installer = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            facts = InstallerProbe.Read(installer);
        }

        if (InstallerSuggestions.For(facts, fileName) is not { } suggestion)
        {
            return null;
        }

        ApplicationDetection? detection = (suggestion.ProductCode, suggestion.UninstallKey) switch
        {
            ({ } productCode, _) => new ProductCodeDetection(productCode),
            (_, { } key) => new UninstallKeyDetection(key),
            _ => null,
        };

        return new ApplicationSuggestion(
            ApplicationKind.Command,
            Name(facts.Name),
            Text(facts.Publisher, ApplicationLimits.MaxPublisherLength),
            Text(facts.Version, ApplicationLimits.MaxVersionLength),
            suggestion.CommandLine,
            suggestion.SuccessExitCodes,
            suggestion.RestartExitCodes,
            detection);
    }

    private static ApplicationSuggestion? Suggest(MsixLayoutResult msix)
    {
        if (msix.Layout?.Main.Manifest is not { } main)
        {
            return null;
        }

        return new ApplicationSuggestion(
            ApplicationKind.Msix,
            Name(main.DisplayName),
            Text(main.PublisherDisplayName, ApplicationLimits.MaxPublisherLength),
            Text(main.Version, ApplicationLimits.MaxVersionLength),
            null,
            [0],
            [3010],
            new PackageFamilyDetection(main.FamilyName));
    }

    private static MsixManifest? ManifestOf(string path)
    {
        using FileStream package = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        return MsixManifestReader.Read(package);
    }

    private static string? Name(string? value) =>
        LibraryEntries.Bounded(ApplicationNames.Clean(value?.Replace(ApplicationNames.Separator, ',')), ApplicationLimits.MaxNameLength);

    private static string? Text(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) ? null : LibraryEntries.Bounded(value.Trim(), maxLength);

    private static bool IsZip(string fileName) => fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    // The browser sends a bare name, but a script may send a path.
    private static string EntryName(string fileName) =>
        Path.GetFileName(fileName.Replace('\\', '/')) is { Length: > 0 } name ? name : "installer";

    private static bool IsRootInstaller(ZipArchiveEntry entry) =>
        entry.FullName.IndexOfAny(['/', '\\']) < 0
        && (entry.FullName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
}
