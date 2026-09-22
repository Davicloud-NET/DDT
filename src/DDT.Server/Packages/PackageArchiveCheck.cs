// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using DDT.Contracts.Packages;

namespace DDT.Server.Packages;

// Every agent unpacks a package as SYSTEM, so a zip is checked here before any machine gets it: its names, which
// must stay inside the folder they are unpacked to, and its sizes and checksums, which are proven by inflating every
// entry into nothing. The server never writes an entry anywhere.
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
            return Refused("The file is not a zip archive, or it is damaged. Upload a zip file.");
        }
    }

    private static PackageInspection Inspect(IReadOnlyList<ZipArchiveEntry> entries, PackageKind kind, CancellationToken cancellationToken)
    {
        if (entries.Count > PackageLimits.MaxEntries)
        {
            return Refused($"The zip holds {entries.Count} entries. A package can hold at most {PackageLimits.MaxEntries}.");
        }

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> filePaths = new(StringComparer.OrdinalIgnoreCase);
        List<ZipArchiveEntry> files = [];
        long declared = 0;

        foreach (ZipArchiveEntry entry in entries)
        {
            string name = entry.FullName;

            if (NameProblem(name) is { } problem)
            {
                return Refused($"The entry {Shown(name)} {problem}");
            }

            if (entry.IsEncrypted)
            {
                return Refused($"The entry {Shown(name)} is encrypted. Upload a zip without a password.");
            }

            if (((entry.ExternalAttributes >> 16) & UnixTypeMask) == UnixSymbolicLink)
            {
                return Refused($"The entry {Shown(name)} is a symbolic link. Put the file itself in the zip.");
            }

            if (!names.Add(PathOf(name)))
            {
                return Refused($"The entry {Shown(name)} is in the zip twice, if case is ignored as Windows ignores it.");
            }

            if (IsFolder(name))
            {
                continue;
            }

            // Checked before adding, so no sum of declared sizes can overflow.
            if (entry.Length < 0 || entry.Length > PackageLimits.MaxExpandedBytes - declared)
            {
                return Refused($"Unpacked, the zip would take more than {PackageLimits.MaxExpandedBytes / (1024 * 1024 * 1024)} GB.");
            }

            declared += entry.Length;
            files.Add(entry);
            filePaths.Add(PathOf(name));
        }

        foreach (string path in filePaths)
        {
            for (int slash = path.IndexOf('/', StringComparison.Ordinal); slash > 0; slash = path.IndexOf('/', slash + 1))
            {
                if (filePaths.Contains(path[..slash]))
                {
                    return Refused($"The zip has a file {Shown(path[..slash])} and a folder of the same name.");
                }
            }
        }

        if (kind == PackageKind.Drivers && !files.Any(f => f.FullName.EndsWith(".inf", StringComparison.OrdinalIgnoreCase)))
        {
            return Refused("A driver package needs at least one .inf file. Zip the folder that holds the drivers' .inf files.");
        }

        return Inflate(files, declared, cancellationToken);
    }

    // The sizes in the zip are the uploader's word. Inflating proves them, so the agent can check its disk space
    // against them and stop unpacking at them. A reader stops at the stated size without an error, so only the
    // checksum shows an entry that holds more than it says.
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
                        crc = ZipCrc32.Append(crc, buffer.AsSpan(0, count));
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
                catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
                {
                    return Refused($"The entry {Shown(file.FullName)} is damaged. Create the zip again.");
                }
                catch (NotSupportedException)
                {
                    return Refused($"The entry {Shown(file.FullName)} is compressed in a way DDT cannot unpack. Create the zip with Deflate.");
                }

                if (actual != file.Length)
                {
                    return Refused(string.Create(
                        CultureInfo.InvariantCulture,
                        $"The entry {Shown(file.FullName)} unpacks to {actual} bytes, but the zip says {file.Length}. Create the zip again."));
                }

                if (crc != file.Crc32)
                {
                    return Refused($"The entry {Shown(file.FullName)} does not unpack to the bytes the zip says it holds. Create the zip again.");
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
    private static string? NameProblem(string name)
    {
        if (name.Length > PackageLimits.MaxEntryNameLength)
        {
            return $"has a name longer than {PackageLimits.MaxEntryNameLength} characters.";
        }

        if (name.Any(char.IsControl))
        {
            return "has a control character in its name.";
        }

        if (name.StartsWith('/') || name.StartsWith('\\'))
        {
            return "starts at the root of a drive.";
        }

        string[] parts = PathOf(name).Split('/');

        if (parts.Length > PackageLimits.MaxDepth)
        {
            return $"is more than {PackageLimits.MaxDepth} folders deep.";
        }

        foreach (string part in parts)
        {
            if (part.Length == 0 || part is "." or "..")
            {
                return "has an empty name, or a folder name that points out of the package.";
            }

            if (part.IndexOfAny(s_forbidden) >= 0)
            {
                return "has a character Windows does not allow in names, such as : for a drive or a data stream.";
            }

            if (part[^1] is '.' or ' ')
            {
                return "has a name that ends in a dot or a space, which Windows cannot create.";
            }

            if (s_deviceNames.Contains(part.Split('.')[0].TrimEnd()))
            {
                return "has a name Windows keeps for a device, such as CON or NUL.";
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

    private static PackageInspection Refused(string reason) => new(0, 0, reason);
}
