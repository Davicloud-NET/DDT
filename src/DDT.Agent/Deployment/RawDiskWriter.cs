// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Disks;

namespace DDT.Agent.Deployment;

// Writes a raw disk image as it arrives but holds back the first mebibyte, the partition table, until Finish writes it
// last: a run that stops leaves a disk that starts nothing rather than half an image that seems whole.
public sealed class RawDiskWriter(IRawDisk disk, AgentLog log)
{
    public const int HeadBytes = GptLayout.MaxHeadBytes;

    private const int ChunkBytes = 4 * 1024 * 1024;
    private const int SectorSize = GptLayout.SectorSize;

    private readonly byte[] _head = new byte[HeadBytes];
    private readonly byte[] _chunk = new byte[ChunkBytes];
    private long _chunkStart = HeadBytes;

    // The bytes of the image taken so far.
    public long Written { get; private set; }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (Written + data.Length > disk.Length)
        {
            throw new DeploymentStepException(
                $"The image holds more than the {ByteSize.Format(disk.Length)} of the disk it is written to. Run the sequence on a larger disk.");
        }

        while (!data.IsEmpty)
        {
            int taken;

            if (Written < HeadBytes)
            {
                taken = (int)Math.Min(data.Length, HeadBytes - Written);
                data[..taken].CopyTo(_head.AsSpan((int)Written));
            }
            else
            {
                int at = (int)(Written - _chunkStart);
                taken = Math.Min(data.Length, ChunkBytes - at);
                data[..taken].CopyTo(_chunk.AsSpan(at));

                if (at + taken == ChunkBytes)
                {
                    disk.Write(_chunkStart, _chunk);
                    _chunkStart += ChunkBytes;
                }
            }

            Written += taken;
            data = data[taken..];
        }
    }

    // Back to the first byte of the image, whose bytes are written again over what is there.
    public void Restart()
    {
        Written = 0;
        _chunkStart = HeadBytes;
    }

    // Writes the rest of the image in whole sectors, then the partition table for this disk: the backup at its end, and
    // the held mebibyte last. Returns the table as the disk has it now.
    public GptLayout Finish()
    {
        if (Written > _chunkStart)
        {
            int rest = (int)(Written - _chunkStart);
            int whole = RoundUp(rest);
            _chunk.AsSpan(rest, whole - rest).Clear();
            disk.Write(_chunkStart, _chunk.AsSpan(0, whole));
        }

        int headLength = (int)Math.Min(Written, HeadBytes);
        GptLayout image;
        GptLayout layout;

        try
        {
            image = GptLayout.Read(_head.AsSpan(0, headLength));
            layout = image.ForDisk(disk.Length / SectorSize);
        }
        catch (Exception exception) when (exception is InvalidGptException or ArgumentException)
        {
            throw new DeploymentStepException($"The image's partition table cannot be read: {exception.Message} Upload the image again.", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new DeploymentStepException(
                $"The image's partitions and its backup table need more than the {ByteSize.Format(disk.Length)} of the disk. Run the sequence on a larger disk.",
                exception);
        }

        disk.Write(layout.BackupEntriesLba * SectorSize, layout.EntryArray());
        disk.Write(layout.BackupLba * SectorSize, layout.BackupHeader());

        // The image's own backup header, now in the middle of the disk, would only mislead a tool that searches for one.
        if (image.BackupLba > layout.LastUsedLba && image.BackupLba < layout.BackupEntriesLba && image.BackupLba * SectorSize < Written)
        {
            disk.Write(image.BackupLba * SectorSize, new byte[SectorSize]);
        }

        byte[] head = new byte[RoundUp(headLength)];
        _head.AsSpan(0, headLength).CopyTo(head);
        layout.ProtectiveMbr(head).CopyTo(head, 0);
        layout.PrimaryHeader().CopyTo(head, SectorSize);
        disk.Write(0, head);
        disk.Flush();

        try
        {
            disk.UpdateProperties();
        }
        catch (DeploymentStepException exception)
        {
            log.Warning($"{exception.Message} The disk holds the image all the same.");
        }

        return layout;
    }

    private static int RoundUp(int length) => (length + SectorSize - 1) / SectorSize * SectorSize;
}
