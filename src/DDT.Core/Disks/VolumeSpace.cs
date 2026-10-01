// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DDT.Core.Disks;

// The space of the volume that holds a folder. On Linux DriveInfo measures the file system of the path it's given. On
// Windows it only takes a drive root, so a folder on a share or a mounted volume asks GetDiskFreeSpaceEx instead.
public static partial class VolumeSpace
{
    public static (long Available, long Total) Of(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);

        if (!OperatingSystem.IsWindows())
        {
            DriveInfo drive = new(folder);

            return (drive.AvailableFreeSpace, drive.TotalSize);
        }

        // A share's root only counts with its last separator
        string path = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;

        if (GetDiskFreeSpaceEx(path, out ulong available, out ulong total, out _) == 0)
        {
            throw new IOException($"Windows doesn't say how much space {folder} has: {new Win32Exception(Marshal.GetLastPInvokeError()).Message}");
        }

        return ((long)Math.Min(available, long.MaxValue), (long)Math.Min(total, long.MaxValue));
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetDiskFreeSpaceEx(string directory, out ulong freeBytesAvailable, out ulong totalBytes, out ulong totalFreeBytes);
}
