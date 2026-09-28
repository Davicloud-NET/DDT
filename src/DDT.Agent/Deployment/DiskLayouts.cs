// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DDT.Agent.Deployment;

// Reads which disk a volume is on, and a disk's partitions with IOCTL_DISK_GET_DRIVE_LAYOUT_EX.
internal sealed class DiskLayouts(AgentLog log)
{
    private const int StorageDeviceNumberLength = 12;
    private const int MaxLayoutLength = 1024 * 1024;

    public static SafeFileHandle OpenDisk(int number) => DiskNativeMethods.CreateFile(
        string.Create(CultureInfo.InvariantCulture, $@"\\.\PhysicalDrive{number}"),
        DiskNativeMethods.GenericRead,
        DiskNativeMethods.FileShareRead | DiskNativeMethods.FileShareWrite,
        0,
        DiskNativeMethods.OpenExisting,
        0,
        0);

    // STORAGE_DEVICE_NUMBER: the device type, then the number of \\.\PhysicalDriveN at 4.
    public static unsafe int ReadDiskNumber(string root)
    {
        using SafeFileHandle volume = PartitionReader.OpenVolume(root);
        byte* number = stackalloc byte[StorageDeviceNumberLength];

        if (!DiskNativeMethods.DeviceIoControl(
            volume,
            DiskNativeMethods.IoctlStorageGetDeviceNumber,
            null,
            0,
            number,
            StorageDeviceNumberLength,
            out uint returned,
            0)
            || returned < StorageDeviceNumberLength)
        {
            throw new DeploymentStepException($"The disk that holds {root} cannot be told (Windows error {Marshal.GetLastPInvokeError()}).");
        }

        return (int)BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(number + 4, 4));
    }

    // Only a cleanup depends on it. A firmware boot entry for an erased EFI system partition is reused instead of
    // staying behind as a dead entry.
    public IReadOnlyList<Guid> ReadSystemPartitionIds(int number)
    {
        using SafeFileHandle handle = OpenDisk(number);

        if (handle.IsInvalid)
        {
            log.Warning($"Disk {number} cannot be opened to read its partitions before it is erased (Windows error {Marshal.GetLastPInvokeError()}).");

            return [];
        }

        if (ReadLayout(handle, number) is not { } layout)
        {
            return [];
        }

        try
        {
            return DriveLayoutReader.EfiSystemPartitionIds(layout);
        }
        catch (ArgumentException exception)
        {
            log.Warning($"The partitions of disk {number} cannot be read ({exception.Message}).");

            return [];
        }
    }

    // Only shown to the technician, so a layout that can't be read counts as no partitions rather than hiding the disk.
    public int ReadPartitionCount(SafeFileHandle handle, int number)
    {
        if (ReadLayout(handle, number) is not { } layout)
        {
            return 0;
        }

        try
        {
            return DriveLayoutReader.CountUsedPartitions(layout);
        }
        catch (ArgumentException exception)
        {
            log.Warning($"The partitions of disk {number} cannot be read ({exception.Message}).");

            return 0;
        }
    }

    // Logs a warning and returns null when the layout can't be read.
    public unsafe byte[]? ReadLayout(SafeFileHandle handle, int number)
    {
        int size = DriveLayoutReader.HeaderLength + (16 * DriveLayoutReader.EntryLength);

        while (size <= MaxLayoutLength)
        {
            byte[] layout = new byte[size];

            fixed (byte* output = layout)
            {
                if (DiskNativeMethods.DeviceIoControl(
                    handle,
                    DiskNativeMethods.IoctlDiskGetDriveLayoutEx,
                    null,
                    0,
                    output,
                    (uint)size,
                    out uint returned,
                    0))
                {
                    return layout[..(int)returned];
                }
            }

            int error = Marshal.GetLastPInvokeError();

            if (error is not (DiskNativeMethods.ErrorInsufficientBuffer or DiskNativeMethods.ErrorMoreData))
            {
                log.Warning($"The partitions of disk {number} cannot be read (Windows error {error}).");

                return null;
            }

            size *= 2;
        }

        log.Warning($"The partitions of disk {number} cannot be read: its layout is larger than {MaxLayoutLength} bytes.");

        return null;
    }
}
