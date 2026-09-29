// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DDT.Contracts.BootImage;
using DDT.Contracts.Packages;
using DDT.Server.Configuration;
using DDT.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DDT.Server.BootImage;

// Compares the driver packages flagged for the boot image with the build in the boot directory. The build's description
// next to boot.wim is read on every request. It's small, and it changes whenever a new build is copied in.
public sealed class BootImageCatalog(IOptions<DdtOptions> options, IConfiguration configuration)
{
    public const string ManifestName = "ddt-boot-image.json";

    // This is the pxe role's DDT:Pxe:BootDirectory setting, see DDT.Pxe.PxeOptions. This project doesn't reference
    // DDT.Pxe. When both roles run in one container, as they do by default, the web role finds the build in the same
    // store.
    private const string BootDirectoryKey = "DDT:Pxe:BootDirectory";

    // A description of one build is a few kilobytes, even with dozens of drivers.
    private const int MaxManifestBytes = 1024 * 1024;

    public string BootDirectory =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(options.Value.StorePath, configuration[BootDirectoryKey] ?? "boot")));

    public string ManifestPath => Path.Combine(BootDirectory, "Boot", ManifestName);

    // Hashes one line per package, sorted by id, so the same set always has the same hash. Returns null when there are
    // no packages.
    public static string? DriverSetHash(IEnumerable<(Guid PackageId, string Sha256)> drivers)
    {
        ArgumentNullException.ThrowIfNull(drivers);

        StringBuilder lines = new();

        foreach ((Guid packageId, string sha256) in drivers.OrderBy(d => d.PackageId.ToString("D"), StringComparer.Ordinal))
        {
            lines.Append(packageId.ToString("D")).Append(' ').Append(sha256.ToLowerInvariant()).Append('\n');
        }

        return lines.Length == 0 ? null : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(lines.ToString())));
    }

    public async Task<BootImageView> ViewAsync(DdtDbContext database, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        List<BootImageDriver> flagged = await database.Packages
            .AsNoTracking()
            .Where(p => p.BootImage && p.Kind == PackageKind.Drivers)
            .Select(p => new BootImageDriver(p.Id, p.Name, p.Sha256, p.SizeBytes))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<BootImageDriver> drivers = [.. flagged.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.PackageId)];
        string? driverSetHash = DriverSetHash(drivers.Select(d => (d.PackageId, d.Sha256)));
        BootImageBuild? build = ReadBuild();
        bool stale = build is null ? drivers.Count > 0 : driverSetHash != build.DriverSetHash;

        return new BootImageView(drivers, driverSetHash, build, stale);
    }

    // Returns null for a missing or broken file, so the page shows no build instead of failing. The hash is computed
    // again from the drivers the file lists, so it's computed the same way as the server's.
    public BootImageBuild? ReadBuild()
    {
        BootImageManifest? manifest;

        try
        {
            FileInfo file = new(ManifestPath);

            if (!file.Exists || file.Length > MaxManifestBytes)
            {
                return null;
            }

            manifest = JsonSerializer.Deserialize(File.ReadAllBytes(file.FullName), BootImageJsonContext.Default.BootImageManifest);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }

        if (manifest?.BuiltUtc is not { } builtUtc)
        {
            return null;
        }

        List<BootImageBuildDriver> drivers = [];

        foreach (BootImageManifestDriver? driver in manifest.Drivers ?? [])
        {
            if (driver?.PackageId is not { } packageId || driver.Sha256 is not { Length: 64 } sha256 || !sha256.All(char.IsAsciiHexDigit))
            {
                return null;
            }

            drivers.Add(new BootImageBuildDriver(packageId, driver.Name ?? string.Empty, sha256.ToLowerInvariant()));
        }

        return new BootImageBuild(
            builtUtc,
            DriverSetHash(drivers.Select(d => (d.PackageId, d.Sha256))),
            drivers,
            manifest.AdkVersion,
            manifest.BootManager,
            manifest.AgentVersion);
    }

    // The watcher compares this to notice a new build without reading the file each time.
    public (bool Exists, DateTime LastWriteUtc, long Length) Stamp()
    {
        try
        {
            FileInfo file = new(ManifestPath);

            return file.Exists ? (true, file.LastWriteTimeUtc, file.Length) : (false, default, 0);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (false, default, 0);
        }
    }
}
