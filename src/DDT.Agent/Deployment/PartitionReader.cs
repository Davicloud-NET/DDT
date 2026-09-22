// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DDT.Agent.Deployment;

// Reads a volume's partition with PARTITION_INFORMATION_EX as winioctl.h lays it out on 64-bit Windows: the style
// first, and for GPT the unique partition GUID at 48, which finds the partition again after a restart.
public static class PartitionReader
{
    public const int PartitionInformationLength = 144;

    private const int PartitionIdOffset = 48;
    private const int GuidLength = 16;

    // The unique GUID of the GPT partition holding the volume at root, such as W:\.
    public static unsafe Guid ReadId(string root)
    {
        using SafeFileHandle volume = OpenVolume(root);
        byte* information = stackalloc byte[PartitionInformationLength];
        ReadInformation(volume, root, information);

        return ParseId(root, new ReadOnlySpan<byte>(information, PartitionInformationLength));
    }

    // partitionInformation is what IOCTL_DISK_GET_PARTITION_INFO_EX returns for the volume. root only names it in
    // messages.
    public static Guid ParseId(string root, ReadOnlySpan<byte> partitionInformation)
    {
        if (partitionInformation.Length < PartitionInformationLength)
        {
            throw new DeploymentStepException($"The disk reports too little about {root} to tell which partition it is.");
        }

        if (BinaryPrimitives.ReadInt32LittleEndian(partitionInformation) != DriveLayoutReader.StyleGpt)
        {
            throw new DeploymentStepException($"{root} is not on a GPT disk, so its partition cannot be found again after a restart.");
        }

        return new Guid(partitionInformation.Slice(PartitionIdOffset, GuidLength));
    }

    // root is a drive, such as S:\.
    internal static SafeFileHandle OpenVolume(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);

        string drive = root.TrimEnd('\\');

        if (drive.Length != 2 || drive[1] != ':' || !char.IsAsciiLetter(drive[0]))
        {
            throw new DeploymentStepException($"{root} is not a drive, so its partition cannot be read.");
        }

        SafeFileHandle volume = DiskNativeMethods.CreateFile(
            $@"\\.\{drive}",
            DiskNativeMethods.GenericRead,
            DiskNativeMethods.FileShareRead | DiskNativeMethods.FileShareWrite,
            0,
            DiskNativeMethods.OpenExisting,
            0,
            0);

        if (volume.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            volume.Dispose();

            throw new DeploymentStepException($"The volume {root} cannot be opened (Windows error {error}).");
        }

        return volume;
    }

    internal static unsafe void ReadInformation(SafeFileHandle volume, string root, byte* information)
    {
        if (!DiskNativeMethods.DeviceIoControl(
            volume,
            DiskNativeMethods.IoctlDiskGetPartitionInfoEx,
            null,
            0,
            information,
            PartitionInformationLength,
            out uint returned,
            0)
            || returned < PartitionInformationLength)
        {
            throw new DeploymentStepException($"The partition of {root} cannot be read (Windows error {Marshal.GetLastPInvokeError()}).");
        }
    }
}
