// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DDT.Agent.Deployment;

// Describes one \\.\PhysicalDriveN and says whether DDT can install on it, logging the reason when not.
internal sealed class DiskProbe(AgentLog log, DiskLayouts layouts)
{
    private const int StoragePropertyQueryLength = 12;
    private const int LengthInformationLength = 8;

    public LocalDisk? Probe(int number)
    {
        using SafeFileHandle handle = DiskLayouts.OpenDisk(number);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();

            if (error is not (DiskNativeMethods.ErrorFileNotFound or DiskNativeMethods.ErrorPathNotFound))
            {
                log.Warning(error == DiskNativeMethods.ErrorAccessDenied
                    ? $"Disk {number} cannot be opened: access is denied. The agent has to run as an administrator."
                    : $"Disk {number} cannot be opened (Windows error {error}). It is left out.");
            }

            return null;
        }

        if (ReadDescriptor(handle) is not { } device)
        {
            log.Warning($"Disk {number} does not describe itself (Windows error {Marshal.GetLastPInvokeError()}). It is left out.");

            return null;
        }

        if (ReadLength(handle) is not { } size)
        {
            log.Warning($"The size of disk {number} cannot be read (Windows error {Marshal.GetLastPInvokeError()}). It is left out.");

            return null;
        }

        int partitions = layouts.ReadPartitionCount(handle, number);
        string? reason = DiskEligibility.ExclusionReason(device.RemovableMedia, device.BusType, size);
        LocalDisk disk = new(number, device.Model, size, device.BusType, partitions);

        log.Information(
            $"{disk.Describe()}, removable {(device.RemovableMedia ? "yes" : "no")}: " +
            (reason is null ? "DDT can install on it." : $"left out because {reason}."));

        return reason is null ? disk : null;
    }

    private static unsafe StorageDeviceInfo? ReadDescriptor(SafeFileHandle handle)
    {
        byte* query = stackalloc byte[StoragePropertyQueryLength];
        new Span<byte>(query, StoragePropertyQueryLength).Clear();

        byte* header = stackalloc byte[StorageDescriptorReader.HeaderLength];

        if (!DiskNativeMethods.DeviceIoControl(
            handle,
            DiskNativeMethods.IoctlStorageQueryProperty,
            query,
            StoragePropertyQueryLength,
            header,
            StorageDescriptorReader.HeaderLength,
            out _,
            0))
        {
            return null;
        }

        int size = Math.Max(StorageDescriptorReader.ReadSize(new ReadOnlySpan<byte>(header, StorageDescriptorReader.HeaderLength)), StorageDescriptorReader.MinimumLength);
        byte[] descriptor = new byte[size];

        fixed (byte* output = descriptor)
        {
            if (!DiskNativeMethods.DeviceIoControl(
                handle,
                DiskNativeMethods.IoctlStorageQueryProperty,
                query,
                StoragePropertyQueryLength,
                output,
                (uint)size,
                out uint returned,
                0)
                || returned < StorageDescriptorReader.MinimumLength)
            {
                return null;
            }

            return StorageDescriptorReader.Read(descriptor.AsSpan(0, (int)returned));
        }
    }

    private static unsafe long? ReadLength(SafeFileHandle handle)
    {
        byte* length = stackalloc byte[LengthInformationLength];

        return DiskNativeMethods.DeviceIoControl(
            handle,
            DiskNativeMethods.IoctlDiskGetLengthInfo,
            null,
            0,
            length,
            LengthInformationLength,
            out uint returned,
            0)
            && returned >= LengthInformationLength
            ? BinaryPrimitives.ReadInt64LittleEndian(new ReadOnlySpan<byte>(length, LengthInformationLength))
            : null;
    }
}
