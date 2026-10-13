// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.Deployment;
using Microsoft.Win32.SafeHandles;

namespace DDT.Agent.LinuxPhase;

// Whether the agent's output is the stream systemd connected to the journal. JOURNAL_STREAM names that stream by its
// device and inode, and stays set for a process whose output someone replaced since, so the two are compared, as
// systemd.exec(5) says to.
internal static class JournalStream
{
    public const string Variable = "JOURNAL_STREAM";

    private const int StandardOutput = 1;

    // The agent writes its lines to its standard output.
    public static bool IsOutput(string? value)
    {
        if (string.IsNullOrEmpty(value) || !OperatingSystem.IsLinux())
        {
            return false;
        }

        using SafeFileHandle output = new(StandardOutput, ownsHandle: false);

        return Names(value, Of(output));
    }

    // Whether the variable's value, "device:inode" in decimal, names the stream.
    internal static bool Names(string? value, (ulong Device, ulong Inode)? stream)
    {
        if (value is null || stream is not { } open || value.Split(':') is not [string device, string inode])
        {
            return false;
        }

        return ulong.TryParse(device, NumberStyles.None, CultureInfo.InvariantCulture, out ulong namedDevice)
            && ulong.TryParse(inode, NumberStyles.None, CultureInfo.InvariantCulture, out ulong namedInode)
            && (namedDevice, namedInode) == open;
    }

    // The device and inode of an open file as stat's st_dev and st_ino give them. Null when the kernel does not say.
    internal static (ulong Device, ulong Inode)? Of(SafeFileHandle file)
    {
        if (LinuxFileNativeMethods.statx(file, string.Empty, LinuxFileNativeMethods.EmptyPath, LinuxFileNativeMethods.BasicStatus, out LinuxFileNativeMethods.Status status) != 0)
        {
            return null;
        }

        return (Device(status.DeviceMajor, status.DeviceMinor), status.Inode);
    }

    // makedev of sys/sysmacros.h, which is how st_dev holds the two numbers.
    private static ulong Device(ulong major, ulong minor) =>
        ((major & 0xFFFFF000) << 32) | ((major & 0x00000FFF) << 8) | ((minor & 0xFFFFFF00) << 12) | (minor & 0x000000FF);
}
