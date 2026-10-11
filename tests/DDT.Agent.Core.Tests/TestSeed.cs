// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent.Deployment;
using DDT.Core.CloudInit;
using DDT.Core.Disks;

namespace DDT.Agent.Tests;

// A disk's cloud-init seed, read back through its partition table and DDT's own FAT reader.
internal sealed class TestSeed
{
    private const int MaxFileBytes = 32 * 1024 * 1024;

    private TestSeed(GptPartition partition, byte[] bytes)
    {
        Partition = partition;
        Bytes = bytes;
        Volume = FatVolume.Open(new MemoryStream(bytes), 0, bytes.Length);
    }

    public GptPartition Partition { get; }

    // The whole partition, also what no file holds any more.
    public byte[] Bytes { get; }

    public FatVolume Volume { get; }

    public static TestSeed Read(IRawDisk disk)
    {
        ArgumentNullException.ThrowIfNull(disk);

        return Read((offset, buffer) => disk.Read(offset, buffer));
    }

    public static TestSeed Read(Stream disk)
    {
        ArgumentNullException.ThrowIfNull(disk);

        return Read((offset, buffer) =>
        {
            disk.Position = offset;
            disk.ReadExactly(buffer);
        });
    }

    public IEnumerable<string> Names(string folder = "") => Volume.List(folder).Select(entry => entry.Name);

    public byte[] File(string path) => Volume.ReadFile(Volume.Find(path) ?? throw new FileNotFoundException($"The seed has no {path}."), MaxFileBytes);

    public string Text(string path) => Encoding.UTF8.GetString(File(path));

    // Whether the bytes are anywhere in the partition, in a file or in what a deleted one left.
    public bool Holds(string text) => Bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(text)) >= 0;

    private static TestSeed Read(Action<long, byte[]> read)
    {
        byte[] head = new byte[GptLayout.MaxHeadBytes];
        read(0, head);
        GptPartition partition = GptLayout.Read(head).Partitions.Single(candidate => candidate.Name == CloudInitSeed.Label);
        byte[] bytes = new byte[partition.Sectors * GptLayout.SectorSize];
        read(partition.FirstLba * GptLayout.SectorSize, bytes);

        return new TestSeed(partition, bytes);
    }
}
