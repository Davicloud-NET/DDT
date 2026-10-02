// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DDT.Contracts.BootImage;
using DDT.Contracts.Packages;
using DDT.Pxe;
using DDT.Server.Configuration;
using DDT.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DDT.Server.BootImage;

// The builds in the boot directory and the driver packages flagged for the boot image. A build's description next to
// its boot.wim is read on every request. It's small, and it changes whenever a new build becomes the current one.
public sealed class BootImageCatalog(IOptions<DdtOptions> options, IConfiguration configuration)
{
    public const string ManifestName = "ddt-boot-image.json";

    // This is the pxe role's DDT:Pxe:BootDirectory setting, see DDT.Pxe.PxeOptions. When both roles run in one
    // process, as they do by default, the web role finds the build in the same store.
    private const string BootDirectoryKey = "DDT:Pxe:BootDirectory";

    // A description of one build is a few kilobytes, even with dozens of drivers.
    private const int MaxManifestBytes = 1024 * 1024;

    public string BootDirectory =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(options.Value.StorePath, configuration[BootDirectoryKey] ?? "boot")));

    // The description of the build that is served.
    public string ManifestPath => ManifestIn(BootBuilds.Serving(BootDirectory));

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

    // The driver packages flagged for the boot image, by name.
    public static async Task<List<BootImageDriver>> FlaggedAsync(DdtDbContext database, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        List<BootImageDriver> flagged = await database.Packages
            .AsNoTracking()
            .Where(p => p.BootImage && p.Kind == PackageKind.Drivers)
            .Select(p => new BootImageDriver(p.Id, p.Name, p.Sha256, p.SizeBytes))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. flagged.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.PackageId)];
    }

    // The build that is served. Null for a missing or broken description, so the page shows no build instead of failing.
    public BootImageBuild? ReadBuild() => ReadBuild(ManifestPath);

    // What could be served, newest first: the builds below "builds", and the files in the boot directory itself.
    public IReadOnlyList<BootImageStoredBuild> Builds()
    {
        string? current = BootBuilds.Current(BootDirectory);
        List<BootImageStoredBuild> builds = [];

        try
        {
            builds.AddRange(BootBuilds.List(BootDirectory)
                .Where(name => File.Exists(ManifestIn(BootBuilds.FolderOf(BootDirectory, name))))
                .Select(name => new BootImageStoredBuild(name, ReadBuild(ManifestIn(BootBuilds.FolderOf(BootDirectory, name)))?.BuiltUtc, name == current)));

            if (File.Exists(Path.Combine(BootDirectory, "Boot", "boot.wim")))
            {
                builds.Add(new BootImageStoredBuild(null, ReadBuild(ManifestIn(BootDirectory))?.BuiltUtc, current is null));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A boot directory that cannot be listed has no builds to offer
        }

        return [.. builds.OrderByDescending(build => build.BuiltUtc ?? DateTimeOffset.MinValue)];
    }

    // Serves another build. Returns false for a name that is none of Builds.
    public bool Use(string? name)
    {
        if (!Builds().Any(build => build.Name == name))
        {
            return false;
        }

        BootBuilds.SetCurrent(BootDirectory, name);

        return true;
    }

    // The build that is served, by its folder. Null while the boot directory itself holds the files.
    public string? Current => BootBuilds.Current(BootDirectory);

    // Removes every build but the current one and the one to go back to.
    public void Prune(string? previous)
    {
        string? current = Current;

        foreach (string name in BootBuilds.List(BootDirectory).Where(name => name != current && name != previous))
        {
            try
            {
                Directory.Delete(BootBuilds.FolderOf(BootDirectory, name), recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A machine still netboots from it. The next build tries again.
            }
        }
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

    // What a folder with a build's Boot, EFI and x64 says about it. Null when it has no readable description.
    public static BootImageBuild? ReadBuildIn(string root) => ReadBuild(ManifestIn(root));

    private static string ManifestIn(string root) => Path.Combine(root, "Boot", ManifestName);

    // The hash is computed again from the drivers the file lists, so it's computed the same way as the server's.
    private static BootImageBuild? ReadBuild(string path)
    {
        BootImageManifest? manifest;

        try
        {
            FileInfo file = new(path);

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
            manifest.AgentVersion,
            manifest.ServerUrl,
            manifest.RootSha256,
            manifest.KeyboardLayout,
            manifest.PowerShell);
    }
}
