// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers;
using System.IO.Compression;
using DDT.Contracts.Messages;
using DDT.Contracts.Packages;
using DDT.Core;

namespace DDT.Server.Packages;

// Every agent unpacks a package as SYSTEM, so a zip is checked before any machine gets it: its names have to stay
// inside the folder they are unpacked to, and inflating every entry into nothing proves its sizes and checksums.
public static class PackageArchiveCheck
{
    private const int BufferBytes = 1024 * 1024;

    // Unix file types sit in the upper half of the external attributes.
    private const int UnixTypeMask = 0xF000;
    private const int UnixSymbolicLink = 0xA000;

    private static readonly HashSet<string> s_deviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    private static readonly char[] s_forbidden = ['<', '>', '"', '|', '?', '*', ':'];

    public static PackageInspection Inspect(Stream zip, PackageKind kind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(zip);

        try
        {
            using ZipArchive archive = new(zip, ZipArchiveMode.Read, leaveOpen: true);

            return Inspect(archive.Entries, kind, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
        {
            return Refused(ServerMessages.PackageNotZip.With());
        }
    }

    private static PackageInspection Inspect(IReadOnlyList<ZipArchiveEntry> entries, PackageKind kind, CancellationToken cancellationToken)
    {
        if (entries.Count > PackageLimits.MaxEntries)
        {
            return Refused(ServerMessages.PackageTooManyEntries.With("count", entries.Count, "max", PackageLimits.MaxEntries));
        }

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> filePaths = new(StringComparer.OrdinalIgnoreCase);
        List<ZipArchiveEntry> files = [];
        long declared = 0;

        foreach (ZipArchiveEntry entry in entries)
        {
            if (EntryProblem(entry, names) is { } problem)
            {
                return Refused(problem);
            }

            if (IsFolder(entry.FullName))
            {
                continue;
            }

            // Checked before adding, so no sum of declared sizes can overflow.
            if (entry.Length < 0 || entry.Length > PackageLimits.MaxExpandedBytes - declared)
            {
                return Refused(ServerMessages.PackageTooLargeUnpacked.With("max", PackageLimits.MaxExpandedBytes / (1024 * 1024 * 1024)));
            }

            declared += entry.Length;
            files.Add(entry);
            filePaths.Add(PathOf(entry.FullName));
        }

        if (FileAndFolder(filePaths) is { } clash)
        {
            return Refused(clash);
        }

        if (kind == PackageKind.Drivers && !files.Any(f => f.FullName.EndsWith(".inf", StringComparison.OrdinalIgnoreCase)))
        {
            return Refused(ServerMessages.PackageNoInf.With());
        }

        return Inflate(files, declared, cancellationToken);
    }

    // Names takes each entry's path, so a second entry of the same path is refused.
    private static ServerMessage? EntryProblem(ZipArchiveEntry entry, HashSet<string> names)
    {
        string name = entry.FullName;

        if (NameProblem(name) is { } problem)
        {
            return problem;
        }

        if (entry.IsEncrypted)
        {
            return ServerMessages.PackageEntryEncrypted.With("entry", Shown(name));
        }

        if (((entry.ExternalAttributes >> 16) & UnixTypeMask) == UnixSymbolicLink)
        {
            return ServerMessages.PackageEntrySymbolicLink.With("entry", Shown(name));
        }

        return names.Add(PathOf(name)) ? null : ServerMessages.PackageEntryTwice.With("entry", Shown(name));
    }

    // A file whose path is also a folder of another file.
    private static ServerMessage? FileAndFolder(HashSet<string> filePaths)
    {
        foreach (string path in filePaths)
        {
            for (int slash = path.IndexOf('/', StringComparison.Ordinal); slash > 0; slash = path.IndexOf('/', slash + 1))
            {
                if (filePaths.Contains(path[..slash]))
                {
                    return ServerMessages.PackageFileAndFolder.With("entry", Shown(path[..slash]));
                }
            }
        }

        return null;
    }

    // The zip's sizes are the uploader's word, which inflating proves, so the agent can check its disk space and stop
    // unpacking at them. A reader stops at the stated size without an error, so only the checksum shows an entry that
    // holds more.
    private static PackageInspection Inflate(List<ZipArchiveEntry> files, long declared, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferBytes);

        try
        {
            foreach (ZipArchiveEntry file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long actual = 0;
                uint crc = 0;

                try
                {
                    using Stream data = file.Open();
                    int count;

                    while ((count = data.Read(buffer, 0, BufferBytes)) > 0)
                    {
                        actual += count;
                        crc = Crc32.Append(crc, buffer.AsSpan(0, count));
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
                catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
                {
                    return Refused(ServerMessages.PackageEntryDamaged.With("entry", Shown(file.FullName)));
                }
                catch (NotSupportedException)
                {
                    return Refused(ServerMessages.PackageEntryCompression.With("entry", Shown(file.FullName)));
                }

                if (actual != file.Length)
                {
                    return Refused(ServerMessages.PackageEntrySize.With("entry", Shown(file.FullName), "actual", actual, "declared", file.Length));
                }

                if (crc != file.Crc32)
                {
                    return Refused(ServerMessages.PackageEntryChecksum.With("entry", Shown(file.FullName)));
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return new PackageInspection(files.Count, declared, null);
    }

    // Each part of the name has to be one Windows can create, inside the folder the package is unpacked to.
    private static ServerMessage? NameProblem(string name)
    {
        string entry = Shown(name);

        if (name.Length > PackageLimits.MaxEntryNameLength)
        {
            return ServerMessages.PackageEntryNameTooLong.With("entry", entry, "max", PackageLimits.MaxEntryNameLength);
        }

        if (name.Any(char.IsControl))
        {
            return ServerMessages.PackageEntryControlCharacter.With("entry", entry);
        }

        if (name.StartsWith('/') || name.StartsWith('\\'))
        {
            return ServerMessages.PackageEntryAtRoot.With("entry", entry);
        }

        string[] parts = PathOf(name).Split('/');

        if (parts.Length > PackageLimits.MaxDepth)
        {
            return ServerMessages.PackageEntryTooDeep.With("entry", entry, "max", PackageLimits.MaxDepth);
        }

        foreach (string part in parts)
        {
            if (part.Length == 0 || part is "." or "..")
            {
                return ServerMessages.PackageEntryEmptyName.With("entry", entry);
            }

            if (part.IndexOfAny(s_forbidden) >= 0)
            {
                return ServerMessages.PackageEntryForbiddenCharacter.With("entry", entry);
            }

            if (part[^1] is '.' or ' ')
            {
                return ServerMessages.PackageEntryTrailingDot.With("entry", entry);
            }

            if (s_deviceNames.Contains(part.Split('.')[0].TrimEnd()))
            {
                return ServerMessages.PackageEntryDeviceName.With("entry", entry);
            }
        }

        return null;
    }

    private static bool IsFolder(string name) => name.EndsWith('/') || name.EndsWith('\\');

    // Windows takes both slashes as separators, and a folder entry ends in one.
    private static string PathOf(string name) => name.Replace('\\', '/').TrimEnd('/');

    private static string Shown(string name)
    {
        string printable = new([.. name.Select(c => char.IsControl(c) ? '?' : c)]);

        return printable.Length <= 120 ? printable : printable[..120] + "...";
    }

    private static PackageInspection Refused(ServerMessage reason) => new(0, 0, reason.Text, reason);
}
