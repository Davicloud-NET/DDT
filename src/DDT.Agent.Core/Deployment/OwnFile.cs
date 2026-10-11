// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent.Deployment;

// A file the agent wrote itself, opened to overwrite its bytes. A script that ran as root may have put anything in
// its place, so on Linux only a regular file with this one name is written to, and never what a link points to.
internal static class OwnFile
{
    // Null when the path holds nothing to overwrite: no file, a link, a pipe, a socket, a device, or a second name
    // of a file. Opening a pipe would wait for ever, and a second name shares its bytes with the first.
    public static FileStream? OpenToOverwrite(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            return File.Exists(path) && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint) ? Open(path) : null;
        }

        int found = LinuxFileNativeMethods.statx(
            LinuxFileNativeMethods.CurrentDirectory,
            path,
            LinuxFileNativeMethods.NoFollow,
            LinuxFileNativeMethods.BasicStatus,
            out LinuxFileNativeMethods.Status named);

        if (Plain(found, named) is not { } file)
        {
            return null;
        }

        FileStream stream = Open(path);

        // What was opened is asked again, in case the name changed hands in between.
        int opened = LinuxFileNativeMethods.statx(
            stream.SafeFileHandle,
            string.Empty,
            LinuxFileNativeMethods.EmptyPath,
            LinuxFileNativeMethods.BasicStatus,
            out LinuxFileNativeMethods.Status held);

        if (Plain(opened, held) == file)
        {
            return stream;
        }

        stream.Dispose();

        return null;
    }

    private static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

    // What tells the file from every other, or null for anything but a regular file with one name.
    private static (uint Major, uint Minor, ulong Inode)? Plain(int result, LinuxFileNativeMethods.Status status)
    {
        if (result != 0)
        {
            int error = Marshal.GetLastPInvokeError();

            // ENOENT and ENOTDIR: nothing is there
            return error is 2 or 20 ? null : throw new IOException(Marshal.GetPInvokeErrorMessage(error));
        }

        bool regular = (status.Mode & LinuxFileNativeMethods.TypeMask) == LinuxFileNativeMethods.RegularFile;

        return regular && status.Links == 1 ? (status.DeviceMajor, status.DeviceMinor, status.Inode) : null;
    }
}
