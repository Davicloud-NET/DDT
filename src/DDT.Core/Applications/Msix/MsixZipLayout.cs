// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;

namespace DDT.Core.Applications.Msix;

// The layout of an application's files as the server stores them, a zip. A package that is compressed in the zip
// cannot be read in place, so it is copied to a file in scratch first, which goes when it is read.
public static class MsixZipLayout
{
    // Call for a zip that passed the package check, so its entries are what they claim. withBundleNames is for the upload's
    // suggestion, see MsixManifestReader.
    public static MsixLayoutResult Read(Stream zip, string scratch, bool withBundleNames = false)
    {
        ArgumentNullException.ThrowIfNull(zip);
        ArgumentException.ThrowIfNullOrEmpty(scratch);

        using ZipArchive archive = new(zip, ZipArchiveMode.Read, leaveOpen: true);
        Dictionary<string, ZipArchiveEntry> files = new(StringComparer.OrdinalIgnoreCase);

        foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => !entry.FullName.EndsWith('/') && !entry.FullName.EndsWith('\\')))
        {
            files.TryAdd(entry.FullName.Replace('\\', '/'), entry);
        }

        return MsixLayouts.Of([.. files.Keys], file => ManifestOf(files[file], scratch, withBundleNames), file => IsLicense(files[file]));
    }

    private static MsixManifest? ManifestOf(ZipArchiveEntry entry, string scratch, bool withBundleNames)
    {
        try
        {
            using Stream data = entry.Open();

            if (data.CanSeek)
            {
                return MsixManifestReader.Read(data, withBundleNames);
            }

            Directory.CreateDirectory(scratch);
            using FileStream copy = new(
                Path.Combine(scratch, $"{Guid.NewGuid():N}.msix"),
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.DeleteOnClose);
            data.CopyTo(copy);
            copy.Position = 0;

            return MsixManifestReader.Read(copy, withBundleNames);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static bool IsLicense(ZipArchiveEntry entry)
    {
        try
        {
            using Stream data = entry.Open();

            return MsixXml.IsLicense(data);
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
