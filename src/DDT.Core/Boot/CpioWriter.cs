// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text;

namespace DDT.Core.Boot;

// Writes a cpio archive in the newc format, which the Linux kernel unpacks as an initramfs. The same entries give the
// same bytes, laid out as build/LinuxBoot/mkcpio.py does: sorted paths, owner 0, one time, inodes counting up.
public static class CpioWriter
{
    private const uint FolderMode = 0x41ED;
    private const uint FileMode = 0x81A4;
    private const string Trailer = "TRAILER!!!";

    public static byte[] Write(IEnumerable<CpioEntry> entries, DateTimeOffset time)
    {
        ArgumentNullException.ThrowIfNull(entries);

        List<(byte[] Name, CpioEntry Entry)> sorted = [.. entries.Select(entry => (Name(entry.Path), entry))];
        sorted.Sort((left, right) => left.Name.AsSpan().SequenceCompareTo(right.Name));

        uint seconds = checked((uint)time.ToUnixTimeSeconds());
        using MemoryStream archive = new();
        uint inode = 0;

        foreach ((byte[] name, CpioEntry entry) in sorted)
        {
            uint links = entry.IsFolder ? 2 + FoldersIn(entry.Path, sorted) : 1;
            uint mode = entry.IsFolder ? FolderMode : FileMode;
            WriteHeader(archive, new Header(++inode, mode, links, seconds, (uint)entry.Content.Length), name);
            archive.Write(entry.Content.Span);
            Pad(archive);
        }

        WriteHeader(archive, new Header(0, 0, 1, 0, 0), Encoding.ASCII.GetBytes(Trailer));

        return archive.ToArray();
    }

    // The kernel creates what a path names below its root, so a path may not leave it.
    private static byte[] Name(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (path.AsSpan().IndexOfAny('\\', '\0') >= 0 || path.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new ArgumentException($"{path} is not a path inside the archive.", nameof(path));
        }

        return Encoding.UTF8.GetBytes(path);
    }

    private static uint FoldersIn(string folder, List<(byte[] Name, CpioEntry Entry)> entries) =>
        (uint)entries.Count(other => other.Entry.IsFolder && Parent(other.Entry.Path) == folder);

    private static string Parent(string path) => path.LastIndexOf('/') is var slash and >= 0 ? path[..slash] : string.Empty;

    // The magic, then thirteen numbers of eight hex digits, the name with its zero, and zeroes up to four bytes.
    private static void WriteHeader(MemoryStream archive, Header header, byte[] name)
    {
        uint[] fields = [header.Inode, header.Mode, 0, 0, header.Links, header.Time, header.Size, 0, 0, 0, 0, (uint)name.Length + 1, 0];

        archive.Write("070701"u8);

        foreach (uint field in fields)
        {
            archive.Write(Encoding.ASCII.GetBytes(field.ToString("X8", CultureInfo.InvariantCulture)));
        }

        archive.Write(name);
        archive.WriteByte(0);
        Pad(archive);
    }

    private static void Pad(MemoryStream archive)
    {
        while (archive.Length % 4 != 0)
        {
            archive.WriteByte(0);
        }
    }

    private readonly record struct Header(uint Inode, uint Mode, uint Links, uint Time, uint Size);
}
