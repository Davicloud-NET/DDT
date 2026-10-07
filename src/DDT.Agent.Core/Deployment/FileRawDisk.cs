// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Win32.SafeHandles;

namespace DDT.Agent.Deployment;

// A file that stands in for a disk of length bytes in 512-byte sectors, for the dry run. It's sparse, so a 128 GiB
// disk only takes up as much space on this computer as was written to it.
public sealed class FileRawDisk : IRawDisk
{
    private readonly FileStream _file;

    // makeSparse asks a system that keeps a file whole unless told otherwise, as Windows does. It runs before the file
    // gets its length.
    public FileRawDisk(string path, long length, Action<SafeFileHandle>? makeSparse = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        Length = length;

        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } directory)
        {
            Directory.CreateDirectory(directory);
        }

        _file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, bufferSize: 0);
        makeSparse?.Invoke(_file.SafeFileHandle);
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
}
