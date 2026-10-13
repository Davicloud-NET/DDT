// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DDT.Agent.Deployment;

// What telling a file from a link, and finding where a folder really is, needs of libc on Linux.
internal static partial class LinuxFileNativeMethods
{
    // AT_FDCWD, AT_SYMLINK_NOFOLLOW and AT_EMPTY_PATH of fcntl.h.
    public const int CurrentDirectory = -100;
    public const int NoFollow = 0x100;
    public const int EmptyPath = 0x1000;

    // STATX_BASIC_STATS of linux/stat.h.
    public const uint BasicStatus = 0x7FF;

    // S_IFMT and S_IFREG of sys/stat.h.
    public const ushort TypeMask = 0xF000;
    public const ushort RegularFile = 0x8000;

    // PATH_MAX of limits.h, and the end of the text.
    public const int MaxPath = 4096 + 1;

    // O_RDONLY with O_CLOEXEC of fcntl.h, which x64 and arm64 number alike.
    public const int ReadOnlyCloseOnExec = 0x80000;

    // ENOENT of errno.h.
    public const int NoSuchFile = 2;

    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int statx(int directory, string path, int flags, uint mask, out Status status);

    // With an empty path and EmptyPath, of the open file itself.
    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int statx(SafeFileHandle file, string path, int flags, uint mask, out Status status);

    // Gives a file a second name, which shares its bytes with the first.
    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int link(string existing, string added);

    // Declared with the mode the kernel reads: open takes a variable number of arguments in C.
    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int open(string path, int flags, int mode);

    // Of a folder: has its names written to the disk.
    [LibraryImport("libc", SetLastError = true)]
    internal static partial int fsync(int file);

    [LibraryImport("libc", SetLastError = true)]
    internal static partial int close(int file);

    // Writes the path without any link in it into resolved, which holds MaxPath bytes. Null when there is no such path.
    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint realpath(string path, ref byte resolved);

    // The kernel's struct statx, which is the same on every architecture. Only what is read is named.
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    internal struct Status
    {
        [FieldOffset(16)]
        public uint Links;

        [FieldOffset(28)]
        public ushort Mode;

        [FieldOffset(32)]
        public ulong Inode;

        [FieldOffset(136)]
        public uint DeviceMajor;

        [FieldOffset(140)]
        public uint DeviceMinor;
    }
}
