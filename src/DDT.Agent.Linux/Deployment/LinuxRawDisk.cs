// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Win32.SafeHandles;

namespace DDT.Agent.Deployment;

// A whole disk through its device, which the agent holds alone while it is open.
public sealed class LinuxRawDisk(SafeFileHandle device, string path, long length, int sectorSize, IBlockDevices devices) : IRawDisk
{
    public int SectorSize => sectorSize;

    public long Length => length;

    public void Read(long offset, Span<byte> buffer)
    {
        Check(offset, buffer.Length, "read");

        try
        {
            while (!buffer.IsEmpty)
            {
                int read = RandomAccess.Read(device, buffer, offset);

                if (read == 0)
                {
                    throw new DeploymentStepException($"{path} ends before its {length} bytes do.");
                }

                offset += read;
                buffer = buffer[read..];
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new DeploymentStepException($"{path} cannot be read at byte {offset} ({exception.Message}).", exception);
        }
    }

    public void Write(long offset, ReadOnlySpan<byte> buffer)
    {
        Check(offset, buffer.Length, "written");

        try
        {
            RandomAccess.Write(device, buffer, offset);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new DeploymentStepException($"{path} cannot be written at byte {offset} ({exception.Message}).", exception);
        }
    }

    public void Flush()
    {
        try
        {
            RandomAccess.FlushToDisk(device);
        }
        catch (IOException exception)
        {
            throw new DeploymentStepException($"{path} did not take everything that was written to it ({exception.Message}).", exception);
        }
    }

    // The kernel keeps the old partitions while one of them is in use.
    public void UpdateProperties()
    {
        try
        {
            devices.ReadPartitionsAgain(device);
        }
        catch (IOException exception)
        {
            throw new DeploymentStepException($"The kernel did not read the partition table of {path} again ({exception.Message}).", exception);
        }
    }

    public void Dispose() => device.Dispose();

    private void Check(long offset, int count, string done)
    {
        if (offset < 0 || offset % sectorSize != 0 || count % sectorSize != 0 || offset + count > length)
        {
            throw new ArgumentException($"{path} is {done} in whole sectors of {sectorSize} bytes, within its {length} bytes.");
        }
    }
}
