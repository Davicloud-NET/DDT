// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DDT.Agent.Deployment;

// A file that stands in for a disk of length bytes in 512-byte sectors, for the dry run. It is sparse on NTFS, so a
// disk of 128 GiB takes on this computer only what is written to it.
public sealed unsafe class FileRawDisk : IRawDisk
{
    private readonly FileStream _file;

    public FileRawDisk(string path, long length)
    {
        Length = length;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, bufferSize: 0);

        if (OperatingSystem.IsWindows())
        {
            MakeSparse(_file.SafeFileHandle);
        }

        _file.SetLength(length);
    }

    public int SectorSize => 512;

    public long Length { get; }

    public void Read(long offset, Span<byte> buffer) => RandomAccess.Read(_file.SafeFileHandle, buffer, offset);

    public void Write(long offset, ReadOnlySpan<byte> buffer)
    {
        if (offset % SectorSize != 0 || buffer.Length % SectorSize != 0 || offset + buffer.Length > Length)
        {
            throw new ArgumentException($"The dry run's disk is written in whole sectors of {SectorSize} bytes, within its {Length} bytes.");
        }

        RandomAccess.Write(_file.SafeFileHandle, buffer, offset);
    }

    public void Flush() => _file.Flush(flushToDisk: true);

    public void UpdateProperties()
    {
    }

    public void Dispose() => _file.Dispose();

    // Not every file system can hold sparse files; the dry run then needs the room itself.
    private static void MakeSparse(SafeFileHandle file) =>
        DiskNativeMethods.DeviceIoControl(file, DiskNativeMethods.FsctlSetSparse, null, 0, null, 0, out _, 0);
}
