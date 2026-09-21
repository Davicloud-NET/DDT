// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DDT.Agent.Deployment;

// Reads where the new EFI system partition lies from its volume, with PARTITION_INFORMATION_EX and DISK_GEOMETRY as
// winioctl.h lays them out on 64-bit Windows.
public static class EspReader
{
    public const int PartitionInformationLength = 144;
    public const int GeometryLength = 24;

    private const int StartingOffsetOffset = 8;
    private const int PartitionLengthOffset = 16;
    private const int PartitionNumberOffset = 24;
    private const int PartitionTypeOffset = 32;
    private const int PartitionIdOffset = 48;
    private const int GuidLength = 16;

    private const int BytesPerSectorOffset = 20;

    // systemRoot is the partition's drive, such as S:\.
    public static unsafe EspPartition Read(string systemRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(systemRoot);

        string drive = systemRoot.TrimEnd('\\');

        if (drive.Length != 2 || drive[1] != ':' || !char.IsAsciiLetter(drive[0]))
        {
            throw new DeploymentStepException($"The system partition {systemRoot} is not a drive, so its place on the disk cannot be read.");
        }

        using SafeFileHandle volume = DiskNativeMethods.CreateFile(
            $@"\\.\{drive}",
            DiskNativeMethods.GenericRead,
            DiskNativeMethods.FileShareRead | DiskNativeMethods.FileShareWrite,
            0,
            DiskNativeMethods.OpenExisting,
            0,
            0);

        if (volume.IsInvalid)
        {
            throw new DeploymentStepException($"The system partition {systemRoot} cannot be opened (Windows error {Marshal.GetLastPInvokeError()}).");
        }

        byte* partition = stackalloc byte[PartitionInformationLength];

        if (!DiskNativeMethods.DeviceIoControl(
            volume,
            DiskNativeMethods.IoctlDiskGetPartitionInfoEx,
            null,
            0,
            partition,
            PartitionInformationLength,
            out uint returned,
            0)
            || returned < PartitionInformationLength)
        {
            throw new DeploymentStepException($"The partition of {systemRoot} cannot be read (Windows error {Marshal.GetLastPInvokeError()}).");
        }

        byte* geometry = stackalloc byte[GeometryLength];

        if (!DiskNativeMethods.DeviceIoControl(
            volume,
            DiskNativeMethods.IoctlDiskGetDriveGeometry,
            null,
            0,
            geometry,
            GeometryLength,
            out returned,
            0)
            || returned < GeometryLength)
        {
            throw new DeploymentStepException($"The sector size of the disk holding {systemRoot} cannot be read (Windows error {Marshal.GetLastPInvokeError()}).");
        }

        return Parse(
            systemRoot,
            new ReadOnlySpan<byte>(partition, PartitionInformationLength),
            new ReadOnlySpan<byte>(geometry, GeometryLength));
    }

    // partitionInformation is what IOCTL_DISK_GET_PARTITION_INFO_EX returns for the volume, geometry what
    // IOCTL_DISK_GET_DRIVE_GEOMETRY returns for its disk. systemRoot only names the partition in messages.
    public static EspPartition Parse(string systemRoot, ReadOnlySpan<byte> partitionInformation, ReadOnlySpan<byte> geometry)
    {
        if (partitionInformation.Length < PartitionInformationLength || geometry.Length < GeometryLength)
        {
            throw new DeploymentStepException($"The disk reports too little about {systemRoot} to tell where it lies.");
        }

        if (BinaryPrimitives.ReadInt32LittleEndian(partitionInformation) != DriveLayoutReader.StyleGpt
            || new Guid(partitionInformation.Slice(PartitionTypeOffset, GuidLength)) != DriveLayoutReader.EfiSystemPartitionType)
        {
            throw new DeploymentStepException($"{systemRoot} is not an EFI system partition on a GPT disk, so no firmware boot entry can point at it.");
        }

        uint bytesPerSector = BinaryPrimitives.ReadUInt32LittleEndian(geometry[BytesPerSectorOffset..]);
        long start = BinaryPrimitives.ReadInt64LittleEndian(partitionInformation[StartingOffsetOffset..]);
        long length = BinaryPrimitives.ReadInt64LittleEndian(partitionInformation[PartitionLengthOffset..]);

        if (bytesPerSector == 0 || start < 0 || length <= 0)
        {
            throw new DeploymentStepException($"The disk reports an impossible place for {systemRoot}.");
        }

        return new EspPartition(
            BinaryPrimitives.ReadUInt32LittleEndian(partitionInformation[PartitionNumberOffset..]),
            (ulong)start / bytesPerSector,
            (ulong)length / bytesPerSector,
            new Guid(partitionInformation.Slice(PartitionIdOffset, GuidLength)));
    }
}
