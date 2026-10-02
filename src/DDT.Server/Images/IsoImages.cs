// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DiscUtils;
using DiscUtils.Iso9660;
using DiscUtils.Udf;

namespace DDT.Server.Images;

// Reads the Windows image out of an installation ISO: sources\install.wim, or install.esd on media from Microsoft's
// download tool. Windows media are UDF, since install.wim outgrew what ISO 9660 holds.
public static class IsoImages
{
    // Where both file systems put the name of their first descriptor
    private const int DescriptorOffset = 16 * 2048 + 1;
    private const int CopyBufferBytes = 1024 * 1024;

    private static readonly string[] s_installImages = ["install.wim", "install.esd"];

    public static async Task<bool> IsIsoAsync(string path, CancellationToken cancellationToken)
    {
        byte[] name = new byte[5];

        await using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 0, useAsync: true);

        if (file.Length < DescriptorOffset + name.Length)
        {
            return false;
        }

        file.Position = DescriptorOffset;

        return await file.ReadAtLeastAsync(name, name.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false) == name.Length
            && (name.AsSpan().SequenceEqual("CD001"u8) || name.AsSpan().SequenceEqual("BEA01"u8));
    }

    // Copies the install image to target and reports the bytes done and their total. Returns false for an ISO that
    // holds none.
    public static Task<bool> ExtractInstallImageAsync(string iso, string target, Action<long, long>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Extract(iso, target, progress, cancellationToken), cancellationToken);

    private static bool Extract(string iso, string target, Action<long, long>? progress, CancellationToken cancellationToken)
    {
        using FileStream file = new(iso, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        using DiscFileSystem disc = UdfReader.Detect(file) ? new UdfReader(file) : new CDReader(file, joliet: true);

        if (InstallImage(disc) is not { } path)
        {
            return false;
        }

        using Stream source = disc.OpenFile(path, FileMode.Open, FileAccess.Read);
        using FileStream copy = new(target, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferBytes);
        byte[] buffer = new byte[CopyBufferBytes];
        long total = source.Length;
        int read;

        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            copy.Write(buffer, 0, read);
            progress?.Invoke(copy.Length, total);
        }

        return true;
    }

    // Names are compared without case, and without the version ISO 9660 puts after a semicolon.
    private static string? InstallImage(DiscFileSystem disc)
    {
        string? sources = disc.GetDirectories(string.Empty).FirstOrDefault(directory => Named(directory, "sources"));

        return sources is null
            ? null
            : s_installImages
                .Select(image => disc.GetFiles(sources).FirstOrDefault(file => Named(file, image)))
                .FirstOrDefault(file => file is not null);
    }

    private static bool Named(string path, string name) =>
        string.Equals(path.TrimEnd('\\').Split('\\')[^1].Split(';')[0], name, StringComparison.OrdinalIgnoreCase);
}
