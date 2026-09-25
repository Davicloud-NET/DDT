// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DDT.Agent.Deployment;

// \\.\PhysicalDriveN without the cache and with write-through, so a write is on the disk when it returns. Unbuffered I/O
// needs aligned memory, so every read and write goes through a buffer of its own.
public sealed unsafe class PhysicalDisk : IRawDisk
{
    // DISK_GEOMETRY_EX: DISK_GEOMETRY, whose BytesPerSector is at 20, then DiskSize at 24.
    private const int GeometryLength = 32;
    private const int BytesPerSectorOffset = 20;
    private const int DiskSizeOffset = 24;
    private const int ChunkBytes = 4 * 1024 * 1024;

    private readonly SafeFileHandle _handle;
    private readonly AlignedBuffer _buffer = new(ChunkBytes);
    private readonly int _number;

    private PhysicalDisk(SafeFileHandle handle, int number, int sectorSize, long length)
    {
        _handle = handle;
        _number = number;
        SectorSize = sectorSize;
        Length = length;
    }

    public int SectorSize { get; }

    public long Length { get; }

    public static PhysicalDisk Open(int number)
    {
        SafeFileHandle handle = DiskNativeMethods.CreateFile(
            $@"\\.\PhysicalDrive{number}",
            DiskNativeMethods.GenericRead | DiskNativeMethods.GenericWrite,
            DiskNativeMethods.FileShareRead | DiskNativeMethods.FileShareWrite,
            0,
            DiskNativeMethods.OpenExisting,
            DiskNativeMethods.FileFlagNoBuffering | DiskNativeMethods.FileFlagWriteThrough,
            0);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();

            throw new DeploymentStepException($"Disk {number} cannot be opened to write it (Windows error {error}). The agent has to run as an administrator.");
        }

        byte* geometry = stackalloc byte[GeometryLength];

        if (!DiskNativeMethods.DeviceIoControl(
            handle,
            DiskNativeMethods.IoctlDiskGetDriveGeometryEx,
            null,
            0,
            geometry,
            GeometryLength,
            out uint returned,
            0)
            || returned < GeometryLength)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();

            throw new DeploymentStepException($"The sector size of disk {number} cannot be read (Windows error {error}).");
        }

        ReadOnlySpan<byte> read = new(geometry, GeometryLength);

        return new PhysicalDisk(
            handle,
            number,
            (int)BinaryPrimitives.ReadUInt32LittleEndian(read[BytesPerSectorOffset..]),
            BinaryPrimitives.ReadInt64LittleEndian(read[DiskSizeOffset..]));
    }

    public void Read(long offset, Span<byte> buffer)
    {
        CheckWhole(offset, buffer.Length);

        for (int done = 0; done < buffer.Length; done += ChunkBytes)
        {
            Span<byte> chunk = _buffer.Span[..Math.Min(ChunkBytes, buffer.Length - done)];

            if (RandomAccess.Read(_handle, chunk, offset + done) != chunk.Length)
            {
                throw new DeploymentStepException($"Disk {_number} ended at byte {offset + done}, before what was to be read.");
            }

            chunk.CopyTo(buffer[done..]);
        }
    }

    public void Write(long offset, ReadOnlySpan<byte> buffer)
    {
        CheckWhole(offset, buffer.Length);

        for (int done = 0; done < buffer.Length; done += ChunkBytes)
        {
            ReadOnlySpan<byte> part = buffer.Slice(done, Math.Min(ChunkBytes, buffer.Length - done));
            Span<byte> chunk = _buffer.Span[..part.Length];
            part.CopyTo(chunk);

            try
            {
                RandomAccess.Write(_handle, chunk, offset + done);
            }
            catch (IOException exception)
            {
                throw new DeploymentStepException($"Disk {_number} could not be written at byte {offset + done} ({exception.Message}).", exception);
            }
        }
    }

    public void Flush()
    {
        if (!DiskNativeMethods.FlushFileBuffers(_handle))
        {
            throw new DeploymentStepException($"Disk {_number} could not be flushed (Windows error {Marshal.GetLastPInvokeError()}).");
        }
    }

    public void UpdateProperties()
    {
        if (!DiskNativeMethods.DeviceIoControl(_handle, DiskNativeMethods.IoctlDiskUpdateProperties, null, 0, null, 0, out _, 0))
        {
            throw new DeploymentStepException(
                $"Windows could not be made to read the partition table of disk {_number} again (Windows error {Marshal.GetLastPInvokeError()}).");
        }
    }

    public void Dispose()
    {
        _handle.Dispose();
        _buffer.Dispose();
    }

    private void CheckWhole(long offset, int length)
    {
        if (offset % SectorSize != 0 || length % SectorSize != 0)
        {
            throw new ArgumentException($"Disk {_number} is read and written in whole sectors of {SectorSize} bytes.");
        }
    }
}
