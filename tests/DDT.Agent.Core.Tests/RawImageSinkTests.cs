// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using DDT.Agent.Deployment;
using DDT.Core.Disks;
using Xunit;
using ZstdSharp;

namespace DDT.Agent.Tests;

public sealed class RawImageSinkTests
{
    // Four times the image, so the backup table moves.
    private const long DiskBytes = TestRawImage.Sectors * 4 * GptLayout.SectorSize;
    private const int Piece = 100_000;

    private readonly AgentLog _log = new(new ImmediateTimeProvider(), TextWriter.Null);

    private static async Task SendAsync(RawImageSink sink, byte[] compressed, int from = 0, int? to = null)
    {
        for (int offset = from; offset < (to ?? compressed.Length); offset += Piece)
        {
            await sink.WriteAsync(compressed.AsMemory(offset, Math.Min(Piece, (to ?? compressed.Length) - offset)), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task WritesTheImageAndItsTableForTheDiskLast()
    {
        TestRawImage image = new();
        MemoryRawDisk disk = new(DiskBytes);
        using RawImageSink sink = new(new RawDiskWriter(disk, _log));

        await SendAsync(sink, image.Compressed);
        GptLayout layout = sink.Finish();

        Assert.Equal(image.Compressed.Length, sink.Length);
        Assert.Equal(image.Layout.Partitions, layout.Partitions);
        Assert.Equal(DiskBytes / GptLayout.SectorSize - 1, layout.BackupLba);

        // The partitions hold what the image does, and the disk reads as a table made for it.
        long partitions = TestRawImage.EspFirst * GptLayout.SectorSize;
        long end = (image.Layout.LastUsedLba + 1) * GptLayout.SectorSize;
        Assert.Equal(image.Disk.AsSpan((int)partitions, (int)(end - partitions)).ToArray(), disk.ReadAt(partitions, (int)(end - partitions)));
        GptLayout read = GptLayout.Read(disk.ReadAt(0, RawDiskWriter.HeadBytes));
        Assert.Equal(layout.BackupLba, read.BackupLba);
        Assert.Equal(image.Layout.Partitions, read.Partitions);

        byte[] backup = disk.ReadAt(layout.BackupLba * GptLayout.SectorSize, GptLayout.SectorSize);
        Assert.Equal(layout.BackupHeader(), backup);
        Assert.Equal(new byte[GptLayout.SectorSize], disk.ReadAt(image.Layout.BackupLba * GptLayout.SectorSize, GptLayout.SectorSize));
        Assert.Equal((uint)(DiskBytes / GptLayout.SectorSize - 1), BinaryPrimitives.ReadUInt32LittleEndian(disk.ReadAt(0, 512).AsSpan(446 + 12)));

        // Windows sees no table until the last write, and the disk is flushed and read again after it.
        Assert.Equal(0, disk.Writes[^1].Offset);
        Assert.Equal(RawDiskWriter.HeadBytes, disk.Writes[^1].Length);
        Assert.DoesNotContain(disk.Writes[..^1], write => write.Offset < RawDiskWriter.HeadBytes);
        Assert.Equal((1, 1), (disk.Flushes, disk.PropertyUpdates));
    }

    [Fact]
    public async Task PadsAnImageThatEndsInsideASector()
    {
        TestRawImage image = new(seed: 2, tail: 100);
        MemoryRawDisk disk = new(DiskBytes);
        using RawImageSink sink = new(new RawDiskWriter(disk, _log));

        await SendAsync(sink, image.Compressed);
        sink.Finish();

        byte[] last = disk.ReadAt(TestRawImage.Sectors * GptLayout.SectorSize, GptLayout.SectorSize);
        Assert.Equal(image.Disk.AsSpan(image.Disk.Length - 100).ToArray(), last[..100]);
        Assert.Equal(new byte[GptLayout.SectorSize - 100], last[100..]);
    }

    [Fact]
    public async Task StartsOverWhenTheServerSendsEverythingAgain()
    {
        TestRawImage image = new(seed: 3);
        MemoryRawDisk disk = new(DiskBytes);
        using RawImageSink sink = new(new RawDiskWriter(disk, _log));

        await SendAsync(sink, image.Compressed, to: image.Compressed.Length / 2);
        sink.Restart();
        Assert.Equal(0, sink.Length);
        await SendAsync(sink, image.Compressed);
        GptLayout layout = sink.Finish();

        Assert.Equal(image.Layout.Partitions, layout.Partitions);
        Assert.Equal(image.Disk.AsSpan(2 * 1024 * 1024, 1024 * 1024).ToArray(), disk.ReadAt(2 * 1024 * 1024, 1024 * 1024));
    }

    [Fact]
    public async Task RefusesDamagedAndTruncatedData()
    {
        TestRawImage image = new(seed: 4);
        byte[] damaged = (byte[])image.Compressed.Clone();
        damaged.AsSpan(damaged.Length / 2, 64).Fill(0xAB);
        using RawImageSink broken = new(new RawDiskWriter(new MemoryRawDisk(DiskBytes), _log));
        using RawImageSink cut = new(new RawDiskWriter(new MemoryRawDisk(DiskBytes), _log));

        DeploymentStepException refusal = await Assert.ThrowsAsync<DeploymentStepException>(() => SendAsync(broken, damaged));
        await SendAsync(cut, image.Compressed, to: image.Compressed.Length - 10);

        Assert.Equal(RawImageSink.DamagedMessage, refusal.Message);
        Assert.StartsWith("The image's zstd data ends before", Assert.Throws<DeploymentStepException>(cut.Finish).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesAnImageLargerThanTheDisk()
    {
        TestRawImage image = new(seed: 5);
        using RawImageSink sink = new(new RawDiskWriter(new MemoryRawDisk(4 * 1024 * 1024), _log));

        DeploymentStepException refusal = await Assert.ThrowsAsync<DeploymentStepException>(() => SendAsync(sink, image.Compressed));

        Assert.StartsWith("The image holds more than the 4 MB of the disk", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TakesADiskAsLargeAsTheImage()
    {
        TestRawImage image = new(seed: 6);
        using RawImageSink sink = new(new RawDiskWriter(new MemoryRawDisk(image.Disk.Length), _log));

        await SendAsync(sink, image.Compressed);

        Assert.Equal(image.Layout.BackupLba, sink.Finish().BackupLba);
    }

    // An image cut right after its last partition fits a disk of its size, but its backup table does not.
    [Fact]
    public async Task RefusesADiskWithoutRoomForTheBackupTable()
    {
        TestRawImage image = new(seed: 7);
        int partitionsEnd = (int)((image.Layout.LastUsedLba + 1) * GptLayout.SectorSize);
        using Compressor compressor = new(3);
        byte[] cut = compressor.Wrap(image.Disk.AsSpan(0, partitionsEnd)).ToArray();
        using RawImageSink sink = new(new RawDiskWriter(new MemoryRawDisk(partitionsEnd), _log));

        await SendAsync(sink, cut);

        Assert.StartsWith(
            "The image's partitions and its backup table need more than",
            Assert.Throws<DeploymentStepException>(sink.Finish).Message,
            StringComparison.Ordinal);
    }
}
