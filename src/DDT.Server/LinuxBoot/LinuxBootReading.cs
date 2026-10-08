// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text.Json;
using DDT.Contracts.Images;
using DDT.Contracts.Messages;
using DDT.Core.Boot;
using DDT.Pxe;
using DDT.Server.Images;

namespace DDT.Server.LinuxBoot;

// One look at the Linux boot environment's folder: what its description says and whether the files it lists are there
// in their sizes. Whether each is the very file the description names is found out in the background.
internal sealed class LinuxBootReading
{
    // The description lists every file and package, which stays far below this.
    private const int MaxManifestBytes = 1024 * 1024;

    private const int MaxShimBytes = 16 * 1024 * 1024;
    private const int MaxNameLength = 64;

    // Without any of these no machine starts: shim, GRUB, the kernel and the two initrds a release brings.
    private static readonly string[] s_needed = [BootFiles.Shim, BootFiles.Grub, GrubConfig.Kernel, "base.img", "ddt.img"];

    private volatile LinuxBootState _state;

    private LinuxBootReading(string folder, (long Length, DateTime WriteTimeUtc) stamp, LinuxBootState state)
    {
        Folder = folder;
        Stamp = stamp;
        _state = state;
    }

    public string Folder { get; }

    // The description's length and time, by which a changed one is told.
    public (long Length, DateTime WriteTimeUtc) Stamp { get; }

    public LinuxBootState State => _state;

    public Task Checked { get; private set; } = Task.CompletedTask;

    public static LinuxBootReading Of(string folder, (long Length, DateTime WriteTimeUtc) stamp)
    {
        if (stamp.Length < 0)
        {
            return new LinuxBootReading(folder, stamp, new LinuxBootState(folder, ServerMessages.LinuxBootMissing.With("folder", folder)));
        }

        if (stamp.Length > MaxManifestBytes || ReadManifest(folder) is not { } manifest || Listed(manifest) is not { } files)
        {
            return new LinuxBootReading(folder, stamp, Damaged(folder, "unreadable", LinuxBootEnvironment.ManifestName));
        }

        LinuxBootReading reading = new(folder, stamp, Describe(folder, manifest, files));

        if (reading.State.Problem is null)
        {
            reading.Checked = Task.Run(() => reading.CheckHashesAsync(files));
        }

        return reading;
    }

    // The files the description lists, or null when it lists none or names one that is no plain file name.
    private static List<ListedFile>? Listed(LinuxBootManifest manifest)
    {
        List<ListedFile> files = [];

        foreach (LinuxBootManifestFile file in manifest.Files ?? [])
        {
            if (file.Name is not { } name || !IsFileName(name))
            {
                return null;
            }

            files.Add(new ListedFile(name, file.Size, file.Sha256));
        }

        return files.Count == 0 ? null : files;
    }

    private static LinuxBootManifest? ReadManifest(string folder)
    {
        try
        {
            byte[] json = File.ReadAllBytes(Path.Combine(folder, LinuxBootEnvironment.ManifestName));

            return JsonSerializer.Deserialize(json, LinuxBootJsonContext.Default.LinuxBootManifest);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static LinuxBootState Describe(string folder, LinuxBootManifest manifest, List<ListedFile> files)
    {
        if (s_needed.FirstOrDefault(needed => files.All(file => file.Name != needed)) is { } notListed)
        {
            return Damaged(folder, "incomplete", notListed);
        }

        if (FindUnfit(folder, files) is { } unfit)
        {
            return unfit;
        }

        UefiCa signedUnder = ShimSignedUnder(folder, files);
        UefiCa described = (manifest.ShimSignedUnder ?? []).Aggregate(UefiCa.None, (cas, name) => cas | Named(name));

        // The description only repeats what the signatures say. Where it says something else, one of the two is wrong.
        if (described != UefiCa.None && described != signedUnder)
        {
            return Damaged(folder, "signatures", BootFiles.Shim);
        }

        return new LinuxBootState(folder, null)
        {
            Files = files.Select(file => file.Name).ToFrozenSet(StringComparer.Ordinal),
            Snapshot = manifest.Snapshot,
            KernelRelease = manifest.KernelRelease,
            ShimVersion = VersionOf(manifest, "shim-signed"),
            GrubVersion = VersionOf(manifest, "grub-efi-amd64-signed"),
            ShimSignedUnder = signedUnder == UefiCa.None ? null : signedUnder,
            Keymaps = [.. (manifest.BaseImage?.Keymaps ?? []).Where(GrubConfig.IsKeymap)],
        };
    }

    // A name of the folder and nothing that leaves it, since netboot serves the listed names to everyone.
    private static bool IsFileName(string name) =>
        name.Length is > 0 and <= MaxNameLength
        && name is not ("." or "..")
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    // The first listed file that is not there or has another size, which costs no reading.
    private static LinuxBootState? FindUnfit(string folder, List<ListedFile> files)
    {
        foreach (ListedFile listed in files)
        {
            try
            {
                FileInfo file = new(Path.Combine(folder, listed.Name));

                if (!file.Exists)
                {
                    return Damaged(folder, "missing", listed.Name);
                }

                if (file.Length != listed.Size)
                {
                    return Damaged(folder, "size", listed.Name);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Damaged(folder, "other", listed.Name);
            }
        }

        return null;
    }

    // Read from the shim's own signatures, as firmware does, with the code that judges uploaded disk images.
    private static UefiCa ShimSignedUnder(string folder, List<ListedFile> files)
    {
        if (files.First(file => file.Name == BootFiles.Shim).Size > MaxShimBytes)
        {
            return UefiCa.None;
        }

        try
        {
            AuthenticodeResult result = Authenticode.Check(File.ReadAllBytes(Path.Combine(folder, BootFiles.Shim)), UefiCertificateAuthorities.Microsoft);

            return (result.Anchors ?? []).Aggregate(UefiCa.None, (cas, anchor) => cas | MicrosoftUefiCa.Of(anchor.RawData));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return UefiCa.None;
        }
    }

    // The subject names of the two certificates, as the build writes them.
    private static UefiCa Named(string authority) => authority switch
    {
        "Microsoft Corporation UEFI CA 2011" => UefiCa.Microsoft2011,
        "Microsoft UEFI CA 2023" => UefiCa.Microsoft2023,
        _ => UefiCa.None,
    };

    private static string? VersionOf(LinuxBootManifest manifest, string package) =>
        manifest.Packages?.FirstOrDefault(listed => listed.Name == package)?.Version;

    private static LinuxBootState Damaged(string folder, string reason, string name) =>
        new(folder, ServerMessages.LinuxBootDamaged.With("folder", folder, "reason", reason, "name", name));

    // The first file whose content is not what the description names makes the whole environment damaged.
    private async Task CheckHashesAsync(List<ListedFile> files)
    {
        foreach (ListedFile listed in files)
        {
            try
            {
                await using FileStream file = new(
                    Path.Combine(Folder, listed.Name),
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                string hash = Convert.ToHexString(await SHA256.HashDataAsync(file).ConfigureAwait(false));

                if (!string.Equals(hash, listed.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    _state = Damaged(Folder, "hash", listed.Name);

                    return;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _state = Damaged(Folder, "other", listed.Name);

                return;
            }
        }
    }

    private sealed record ListedFile(string Name, long Size, string? Sha256);
}
