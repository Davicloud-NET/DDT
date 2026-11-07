// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using DDT.Agent.Deployment;

namespace DDT.Agent.Sequences;

// Unpacks a package zip into a new directory. The agent runs as SYSTEM, so it checks every entry's name and size before
// it writes anything, even though the server checked the package at upload. It checks each entry's content as it
// unpacks it too.
public static class PackageExtractor
{
    private const int BufferSize = 1024 * 1024;

    private static readonly HashSet<string> s_deviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    private static readonly char[] s_invalidNameCharacters = Path.GetInvalidFileNameChars();

    // name says what the package is in messages. availableBytes is what the target's disk has free.
    public static async Task ExtractAsync(string zipPath, string target, string name, long availableBytes, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(zipPath);
        ArgumentException.ThrowIfNullOrEmpty(target);

        string root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
        ZipArchive archive;

        try
        {
            archive = ZipFile.OpenRead(zipPath);
        }
        catch (InvalidDataException exception)
        {
            throw new DeploymentStepException($"The {name} is not a zip file ({exception.Message}).", exception);
        }

        using (archive)
        {
            Contents contents = ContentsOf(archive, root, name);

            if (contents.DeclaredBytes > availableBytes)
            {
                throw new DeploymentStepException(
                    $"The {name} unpacks to {ByteSize.Format(contents.DeclaredBytes)}, but the disk has only {ByteSize.Format(availableBytes)} free.");
            }

            Directory.CreateDirectory(root);

            foreach (string directory in contents.Directories)
            {
                Directory.CreateDirectory(directory);
            }

            foreach ((ZipArchiveEntry entry, string path) in contents.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await CopyAsync(entry, path, name, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // Checks every entry before anything is written.
    private static Contents ContentsOf(ZipArchive archive, string root, string name)
    {
        List<(ZipArchiveEntry Entry, string Path)> files = [];
        List<string> directories = [];
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        long declared = 0;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            bool directory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            string path = PathOf(entry.FullName, root, name);

            if (directory)
            {
                directories.Add(path);

                continue;
            }

            if (entry.Length < 0)
            {
                throw new DeploymentStepException($"The {name} declares an impossible size for {entry.FullName}.");
            }

            if (!paths.Add(path))
            {
                throw new DeploymentStepException($"The {name} holds {entry.FullName} twice, so it is not unpacked.");
            }

            // Saturates rather than wrapping, so sizes made to overflow still count as too much.
            declared += Math.Min(entry.Length, long.MaxValue - declared);
            files.Add((entry, path));
        }

        return new Contents(files, directories, declared);
    }

    private static string PathOf(string entryName, string root, string name)
    {
        string[] segments = entryName.Replace('\\', '/').TrimEnd('/').Split('/');

        if (entryName.Length == 0 || entryName[0] is '/' or '\\' || segments.Any(segment => !IsSafe(segment)))
        {
            throw new DeploymentStepException($"The {name} holds an entry named {entryName}, which the agent does not unpack.");
        }

        string path = Path.GetFullPath(Path.Combine([root, .. segments]));

        // Belt and braces after the checks above: nothing lands outside the target.
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new DeploymentStepException($"The {name} holds an entry named {entryName}, which would land outside its directory.");
        }

        return path;
    }

    // Windows drops a trailing dot or space, so "a." would clash with "a". And Windows opens a device for a name such
    // as "con.txt" in any directory.
    private static bool IsSafe(string segment) =>
        segment.Length > 0
        && segment is not ("." or "..")
        && segment[^1] is not ('.' or ' ')
        && segment.IndexOfAny(s_invalidNameCharacters) < 0
        && !s_deviceNames.Contains(segment.Split('.')[0].TrimEnd());

    private static async Task CopyAsync(ZipArchiveEntry entry, string path, string name, CancellationToken cancellationToken)
    {
        Stream source;

        try
        {
            source = entry.Open();
        }
        catch (InvalidDataException exception)
        {
            throw new DeploymentStepException($"The {name} is damaged at {entry.FullName} ({exception.Message}).", exception);
        }

        await using (source.ConfigureAwait(false))
        {
            FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 0, useAsync: true);

            await using (file.ConfigureAwait(false))
            {
                byte[] buffer = new byte[BufferSize];
                long written = 0;
                int read;

                while (true)
                {
                    try
                    {
                        read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    }
                    catch (InvalidDataException exception)
                    {
                        throw new DeploymentStepException($"The {name} is damaged at {entry.FullName} ({exception.Message}).", exception);
                    }

                    if (read == 0)
                    {
                        break;
                    }

                    written += read;

                    if (written > entry.Length)
                    {
                        throw new DeploymentStepException(
                            $"The {name} holds more at {entry.FullName} than the {entry.Length} bytes it declares, so it is not unpacked.");
                    }

                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private sealed record Contents(IReadOnlyList<(ZipArchiveEntry Entry, string Path)> Files, IReadOnlyList<string> Directories, long DeclaredBytes);
}
