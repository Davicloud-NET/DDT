// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Server.Machines;
using Microsoft.Extensions.Options;

namespace DDT.Server.LinuxBoot;

// The Linux boot environment a release puts next to the server: its loaders, kernel and initrds, with a description.
// The description is read again only when its file changed, since every list of sequences asks.
public sealed class LinuxBootEnvironment(IOptions<LinuxBootOptions> options, BundledReleases bundled)
{
    public const string FolderName = "linux-boot";

    public const string ManifestName = "ddt-linux-boot.json";

    // The description lists every file and package, which stays far below this.
    private const int MaxManifestBytes = 1024 * 1024;

    private readonly Lock _lock = new();
    private (string Path, long Length, DateTime WriteTimeUtc, bool Readable)? _read;

    // True when configuration names the folder, as a server run from source does.
    public bool Configured => !string.IsNullOrWhiteSpace(options.Value.Path);

    public string Folder => Path.GetFullPath(Configured ? options.Value.Path : Path.Combine(bundled.Folder, FolderName));

    public string ManifestPath => Path.Combine(Folder, ManifestName);

    // Whether the folder holds a description DDT can read. The files it lists are not checked yet.
    public bool Available
    {
        get
        {
            try
            {
                FileInfo file = new(ManifestPath);

                return file.Exists && file.Length <= MaxManifestBytes && Readable(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    private bool Readable(FileInfo file)
    {
        lock (_lock)
        {
            if (_read is { } read && read.Path == file.FullName && read.Length == file.Length && read.WriteTimeUtc == file.LastWriteTimeUtc)
            {
                return read.Readable;
            }
        }

        bool readable = Read(file.FullName) is not null;

        lock (_lock)
        {
            _read = (file.FullName, file.Length, file.LastWriteTimeUtc, readable);
        }

        return readable;
    }

    private static LinuxBootManifest? Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllBytes(path), LinuxBootJsonContext.Default.LinuxBootManifest);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
