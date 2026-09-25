// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using System.Security.Cryptography;
using DDT.Core.Disks;
using ZstdSharp;

namespace DDT.Server.Tests;

// Raw disk images laid out as a distribution's cloud image is: an EFI system partition at 1 MiB with the given files,
// and a root file system after it, of which the first mebibyte holds random bytes.
internal static class TestDisk
{
    public const long Sectors = 32768;
    public const long EspFirst = 2048;
    public const long EspSectors = 8192;

    public static byte[] Create(IReadOnlyDictionary<string, byte[]> espFiles, int seed = 1, bool withEsp = true)
    {
        ArgumentNullException.ThrowIfNull(espFiles);

        Random random = new(seed);
        GptLayout layout = GptLayout.Create(Sectors, Id(random))
            .WithPartition(withEsp ? GptPartitionTypes.EfiSystem : GptPartitionTypes.BasicData, Id(random), "EFI", EspFirst, EspFirst + EspSectors - 1)
            .WithPartition(GptPartitionTypes.LinuxFileSystem, Id(random), "root", EspFirst + EspSectors, Sectors - 2048);
        byte[] disk = new byte[Sectors * GptLayout.SectorSize];
        Place(disk, 0, layout.ProtectiveMbr(default));
        Place(disk, 1, layout.PrimaryHeader());
        Place(disk, layout.EntriesLba, layout.EntryArray());
        Place(disk, layout.BackupEntriesLba, layout.EntryArray());
        Place(disk, layout.BackupLba, layout.BackupHeader());

        FatVolumeBuilder esp = new(EspSectors * GptLayout.SectorSize, "ESP", (uint)seed, new DateTime(2026, 9, 1)) { HiddenSectors = EspFirst };

        foreach ((string path, byte[] content) in espFiles)
        {
            esp.AddFile(path, content);
        }

        Place(disk, EspFirst, esp.Build());
        random.NextBytes(disk.AsSpan((int)((EspFirst + EspSectors) * GptLayout.SectorSize), 1024 * 1024));

        return disk;
    }

    // The same disk as an image for disks with 4 KiB sectors would have its table.
    public static byte[] ForFourKilobyteSectors(byte[] disk)
    {
        ArgumentNullException.ThrowIfNull(disk);

        byte[] moved = (byte[])disk.Clone();
        moved.AsSpan(GptLayout.SectorSize, GptLayout.SectorSize).Clear();
        disk.AsSpan(GptLayout.SectorSize, GptLayout.SectorSize).CopyTo(moved.AsSpan(4096));

        return moved;
    }

    public static byte[] Gzip(byte[] disk)
    {
        using MemoryStream compressed = new();

        using (GZipStream gzip = new(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(disk);
        }

        return compressed.ToArray();
    }

    public static byte[] Zstd(byte[] disk)
    {
        using Compressor compressor = new(3);

        return compressor.Wrap(disk).ToArray();
    }

    public static byte[] Unzstd(byte[] compressed)
    {
        using MemoryStream disk = new();

        using (DecompressionStream zstd = new(new MemoryStream(compressed)))
        {
            zstd.CopyTo(disk);
        }

        return disk.ToArray();
    }

    public static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    private static Guid Id(Random random)
    {
        byte[] bytes = new byte[16];
        random.NextBytes(bytes);

        return new Guid(bytes);
    }

    private static void Place(byte[] disk, long lba, ReadOnlySpan<byte> data) =>
        data.CopyTo(disk.AsSpan((int)(lba * GptLayout.SectorSize)));
}
