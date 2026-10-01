// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using DDT.Server.BootImage;

namespace DDT.Host.Helper;

// Unpacks the driver packages a build names, from the store into the helper's own folder, as Build-BootImage.ps1
// -ServerDriverPath takes them. A package is the file under its hash and nothing else: the hash is checked again.
public static class DriverPackages
{
    public const string ListName = "drivers.json";

    // Returns null, or what stopped it.
    public static async Task<string?> UnpackAsync(HelperPaths paths, HelperRequest request, string folder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(request);

        Directory.CreateDirectory(folder);

        foreach (HelperDriver driver in request.Drivers)
        {
            string package = paths.ObjectPath(driver.Sha256.ToLowerInvariant());

            if (!File.Exists(package) || File.GetAttributes(package).HasFlag(FileAttributes.ReparsePoint))
            {
                return $"The store has no driver package {driver.Name}.";
            }

            FileStream zip = new(package, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);

            await using (zip.ConfigureAwait(false))
            {
                string hash = Convert.ToHexString(await SHA256.HashDataAsync(zip, cancellationToken).ConfigureAwait(false));

                if (!string.Equals(hash, driver.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return $"The driver package {driver.Name} in the store is not the file its name says.";
                }

                // ExtractToDirectory refuses an entry that would land outside the folder
                zip.Position = 0;
                using ZipArchive archive = new(zip, ZipArchiveMode.Read, leaveOpen: true);
                archive.ExtractToDirectory(Path.Combine(folder, driver.PackageId.ToString("D")));
            }
        }

        HelperDriverList list = new(request.DriverSetHash, request.Drivers);
        await File.WriteAllTextAsync(
            Path.Combine(folder, ListName),
            JsonSerializer.Serialize(list, HelperJsonContext.Default.HelperDriverList),
            cancellationToken).ConfigureAwait(false);

        return null;
    }
}
