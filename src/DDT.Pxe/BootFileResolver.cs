// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics.CodeAnalysis;

namespace DDT.Pxe;

// Resolves the name a client asks for to a file of the build that is served, or refuses. TFTP and HTTP boot share it,
// so neither serves what the other refuses. Each segment must match an entry in the directory, so "..", an 8.3 name, a
// stream or a device name can never alias a file or leave the root.
public sealed class BootFileResolver
{
    private const int MaxRequestLength = 512;

    private readonly string _root;

    public BootFileResolver(string bootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bootDirectory);

        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(bootDirectory));
    }

    // The boot directory
    public string Root => _root;

    // The folder of the current build, or the boot directory itself where it holds the files
    public string Serving => BootBuilds.Serving(_root);

    public bool TryResolve(string requested, [NotNullWhen(true)] out FileInfo? file)
    {
        file = null;

        if (string.IsNullOrEmpty(requested) || requested.Length > MaxRequestLength)
        {
            return false;
        }

        // Windows boot components send backslashes and a leading separator: the boot manager asks for
        // "\Boot\BCD". The leading separator means the TFTP root, never the filesystem root.
        string[] segments = requested.Replace('\\', '/').TrimStart('/').Split('/');

        try
        {
            DirectoryInfo directory = new(Serving);

            if (!directory.Exists)
            {
                return false;
            }

            for (int index = 0; index < segments.Length; index++)
            {
                FileSystemInfo? entry = FindEntry(directory, segments[index]);

                // A junction or symlink inside the boot directory can point anywhere on the volume.
                if (entry is null || entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    return false;
                }

                bool last = index == segments.Length - 1;

                if (last && entry is FileInfo found)
                {
                    file = found;

                    return true;
                }

                if (last || entry is not DirectoryInfo child)
                {
                    return false;
                }

                directory = child;
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    // An exact match wins, or else a single case insensitive one. Firmware written for Windows servers asks for
    // "\Boot\BCD", whatever the file on a Linux host is called. Two names that differ only in case are refused.
    private static FileSystemInfo? FindEntry(DirectoryInfo directory, string name)
    {
        if (name.Length == 0 || name is "." or "..")
        {
            return null;
        }

        FileSystemInfo? match = null;
        bool ambiguous = false;

        foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
        {
            if (string.Equals(entry.Name, name, StringComparison.Ordinal))
            {
                return entry;
            }

            if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                ambiguous = match is not null;
                match = entry;
            }
        }

        return ambiguous ? null : match;
    }
}
