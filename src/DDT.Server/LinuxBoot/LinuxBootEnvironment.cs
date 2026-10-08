// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Server.Machines;
using Microsoft.Extensions.Options;

namespace DDT.Server.LinuxBoot;

// The Linux boot environment a release puts next to the server: its loaders, kernel and initrds, with a description.
// rootCertificate gives the root the machines are handed, or null when the server has none of its own.
public sealed class LinuxBootEnvironment(IOptions<LinuxBootOptions> options, BundledReleases bundled, Func<string?> rootCertificate)
{
    public const string FolderName = "linux-boot";

    public const string ManifestName = "ddt-linux-boot.json";

    private readonly Lock _lock = new();
    private LinuxBootReading? _reading;

    // True when configuration names the folder, as a server run from source does.
    public bool Configured => !string.IsNullOrWhiteSpace(options.Value.Path);

    public string Folder => Path.GetFullPath(Configured ? options.Value.Path : Path.Combine(bundled.Folder, FolderName));

    public string ManifestPath => Path.Combine(Folder, ManifestName);

    // Whether machines can be started in it now.
    public bool Available => Current.Available;

    // What the folder holds, read again only when the description changed, since every list of sequences asks.
    public LinuxBootState Current
    {
        get
        {
            LinuxBootState state = Read().State;

            return state.Problem is null && string.IsNullOrWhiteSpace(rootCertificate())
                ? state with { Problem = ServerMessages.LinuxBootNoRoot.With() }
                : state;
        }
    }

    // The root certificate the machines are handed, in PEM. Null while there is none.
    public string? RootCertificatePem => rootCertificate();

    // Ends once every file's hash was compared with the description, which happens in the background.
    public Task CheckedAsync() => Read().Checked;

    private LinuxBootReading Read()
    {
        string folder = Folder;
        (long Length, DateTime WriteTimeUtc) stamp = Stamp(Path.Combine(folder, ManifestName));

        lock (_lock)
        {
            if (_reading is { } known && known.Folder == folder && known.Stamp == stamp)
            {
                return known;
            }
        }

        LinuxBootReading reading = LinuxBootReading.Of(folder, stamp);

        lock (_lock)
        {
            _reading = reading;
        }

        return reading;
    }

    // A description that is not there has no length, which is a stamp too.
    private static (long Length, DateTime WriteTimeUtc) Stamp(string manifest)
    {
        try
        {
            FileInfo file = new(manifest);

            return file.Exists ? (file.Length, file.LastWriteTimeUtc) : (-1, default);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (-1, default);
        }
    }
}
