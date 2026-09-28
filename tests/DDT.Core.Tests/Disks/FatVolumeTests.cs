// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;
using DDT.Core.Disks;
using Xunit;

namespace DDT.Core.Tests.Disks;

public sealed class FatVolumeTests
{
    private const long Offset = 1024 * 1024;
    private const int MaxBytes = 1024 * 1024;

    private static readonly DateTime s_timestamp = new(2026, 9, 25, 10, 30, 12, DateTimeKind.Utc);
    private static readonly byte[] s_loader = Content(5000, 7);

    private static byte[] Content(int length, int seed)
    {
        byte[] content = new byte[length];
        new Random(seed).NextBytes(content);

        return content;
    }

    // The volume one mebibyte into a larger stream, as a partition lies on a disk.
    private static FatVolume Open(byte[] volume)
    {
        byte[] disk = new byte[Offset + volume.Length + 4096];
        volume.CopyTo(disk, Offset);

        return FatVolume.Open(new MemoryStream(disk), Offset, volume.Length);
    }

    private static FatVolumeBuilder EspBuilder(long size, FatType? type = null)
    {
        FatVolumeBuilder builder = new(size, "ESP", 0x1234ABCD, s_timestamp) { Type = type, HiddenSectors = 2048 };
        builder.AddFile(@"EFI\BOOT\BOOTX64.EFI", s_loader);
        builder.AddFile("EFI/ubuntu/shimx64.efi", Content(3000, 8));
        builder.AddFile(@"EFI\ubuntu\grub.cfg", "search --fs-uuid\n"u8.ToArray());
        builder.AddFile(@"EFI\ubuntu\empty", []);

        return builder;
    }

    [Theory]
    [InlineData(1024 * 1024, null, FatType.Fat12)]
    [InlineData(64 * 1024 * 1024, null, FatType.Fat16)]
    [InlineData(40 * 1024 * 1024, FatType.Fat32, FatType.Fat32)]
    public void ReadsWhatTheBuilderLaidOut(long size, FatType? asked, FatType expected)
    {
        FatVolume volume = Open(EspBuilder(size, asked).Build());

        Assert.Equal(expected, volume.Type);
        Assert.Equal("ESP", volume.Label);
        Assert.Equal(["EFI"], volume.List("").Select(entry => entry.Name));
        Assert.Equal(["BOOT", "ubuntu"], volume.List("EFI").Select(entry => entry.Name));
        Assert.Equal(["shimx64.efi", "grub.cfg", "empty"], volume.List(@"\EFI\ubuntu\").Select(entry => entry.Name));
        Assert.Equal(s_loader, volume.ReadFile(volume.Find(@"efi\boot\bootx64.efi")!, MaxBytes));
        Assert.Equal(Content(3000, 8), volume.ReadFile(volume.Find("EFI/Ubuntu/SHIMX64.EFI")!, MaxBytes));
        Assert.Equal("search --fs-uuid\n"u8.ToArray(), volume.ReadFile(volume.Find(@"EFI\ubuntu\grub.cfg")!, MaxBytes));
        Assert.Empty(volume.ReadFile(volume.Find(@"EFI\ubuntu\empty")!, MaxBytes));
        Assert.Null(volume.Find(@"EFI\BOOT\BOOTAA64.EFI"));
        Assert.Null(volume.Find(@"EFI\BOOT\BOOTX64.EFI\more"));
        Assert.True(volume.Find(@"EFI\BOOT")!.IsDirectory);
    }

    // FAT12 packs two entries into three bytes, so chains that start on an even and on an odd cluster must both
    // survive.
    [Fact]
    public void ReadsFat12ChainsThroughOddAndEvenClusters()
    {
        FatVolumeBuilder builder = new(1024 * 1024, "ESP", 1, s_timestamp) { Type = FatType.Fat12 };
        builder.AddFile("ONE.BIN", Content(1500, 1));
        builder.AddFile("TWO.BIN", Content(2100, 2));

        FatVolume volume = Open(builder.Build());
        FatEntry one = volume.Find("ONE.BIN")!;
        FatEntry two = volume.Find("TWO.BIN")!;

        Assert.Equal(FatType.Fat12, volume.Type);
        Assert.Equal(0u, one.FirstCluster % 2);
        Assert.Equal(1u, two.FirstCluster % 2);
        Assert.Equal(Content(1500, 1), volume.ReadFile(one, MaxBytes));
        Assert.Equal(Content(2100, 2), volume.ReadFile(two, MaxBytes));
    }

    [Fact]
    public void WritesTheBootSectorFieldsLinuxAndWindowsRead()
    {
        byte[] volume = EspBuilder(64 * 1024 * 1024).Build();

        Assert.Equal(512, BinaryPrimitives.ReadUInt16LittleEndian(volume.AsSpan(11)));
        Assert.Equal(2048u, BinaryPrimitives.ReadUInt32LittleEndian(volume.AsSpan(28)));
        Assert.Equal(0x1234ABCDu, BinaryPrimitives.ReadUInt32LittleEndian(volume.AsSpan(39)));
        Assert.Equal("ESP        FAT16   ", Encoding.ASCII.GetString(volume, 43, 19));
        Assert.Equal([0x55, 0xAA], volume.AsSpan(510, 2).ToArray());
    }

    [Fact]
    public void WritesLongNamesOverSeveralEntriesAndKeepsShortNamesApart()
    {
        FatVolumeBuilder builder = new(8 * 1024 * 1024, "CIDATA", 1, s_timestamp);
        string longName = "a-name-longer-than-twenty-six-characters.yaml";
        builder.AddFile("user-data1", "one"u8.ToArray());
        builder.AddFile("user-data2", "two"u8.ToArray());
        builder.AddFile(longName, "three"u8.ToArray());
        builder.AddFile("META", "four"u8.ToArray());

        byte[] built = builder.Build();
        FatVolume volume = Open(built);

        Assert.Equal("CIDATA", volume.Label);
        Assert.Equal(["user-data1", "user-data2", longName, "META"], volume.List("").Select(entry => entry.Name));
        Assert.Equal("two"u8.ToArray(), volume.ReadFile(volume.Find("USER-DATA2")!, MaxBytes));
        Assert.Contains("USER-D~1   ", Encoding.ASCII.GetString(built), StringComparison.Ordinal);
        Assert.Contains("USER-D~2   ", Encoding.ASCII.GetString(built), StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsShortNamesWindowsMarksAsLowerCase()
    {
        FatVolumeBuilder builder = new(1024 * 1024, "ESP", 1, s_timestamp);
        builder.AddFile("GRUBX64.EFI", "grub"u8.ToArray());
        byte[] built = builder.Build();
        int entry = Encoding.ASCII.GetString(built).IndexOf("GRUBX64 EFI", StringComparison.Ordinal);
        built[entry + 12] = 0x18;

        Assert.Equal("grubx64.efi", Assert.Single(Open(built).List("")).Name);
    }

    // Linux and EDK2 take a volume whose 16-bit FAT size is 0 as FAT32 whatever its cluster count, as mkfs.fat -F 32
    // makes on a small partition. The builder never makes one, so this one is cut short to 30,000 clusters.
    [Fact]
    public void ReadsAFat32VolumeWithFewClustersAsFat32()
    {
        byte[] volume = EspBuilder(40 * 1024 * 1024, FatType.Fat32).Build();
        int sectorsPerCluster = volume[13];
        int reserved = BinaryPrimitives.ReadUInt16LittleEndian(volume.AsSpan(14));
        uint fatSectors = BinaryPrimitives.ReadUInt32LittleEndian(volume.AsSpan(36));
        long firstData = reserved + (volume[16] * (long)fatSectors);
        BinaryPrimitives.WriteUInt32LittleEndian(volume.AsSpan(32), (uint)(firstData + (30_000L * sectorsPerCluster)));

        FatVolume read = Open(volume);

        Assert.Equal(FatType.Fat32, read.Type);
        Assert.Equal(s_loader, read.ReadFile(read.Find(@"EFI\BOOT\BOOTX64.EFI")!, MaxBytes));
    }

    // A long name part numbered 0 after a complete long name, with its checksum: a damaged or hostile directory.
    [Fact]
    public void ReadsADirectoryWithALongNamePartNumberedZero()
    {
        FatVolumeBuilder builder = new(1024 * 1024, "ESP", 1, s_timestamp);
        builder.AddFile("a b.txt", [1, 2, 3]);
        byte[] volume = builder.Build();
        int reserved = BinaryPrimitives.ReadUInt16LittleEndian(volume.AsSpan(14));
        int fatSectors = BinaryPrimitives.ReadUInt16LittleEndian(volume.AsSpan(22));
        int root = (reserved + (volume[16] * fatSectors)) * 512;
        int longPart = Enumerable.Range(0, 16).Select(index => root + (index * 32)).First(entry => volume[entry + 11] == 0x0F);

        // The short entry moves down one place, and a copy of the long name part numbered 0 takes its place.
        volume.AsSpan(longPart + 32, 32).CopyTo(volume.AsSpan(longPart + 64, 32));
        volume.AsSpan(longPart, 32).CopyTo(volume.AsSpan(longPart + 32, 32));
        volume[longPart + 32] = 0x20;

        FatVolume read = Open(volume);

        Assert.Single(read.List(""), entry => !entry.IsDirectory);
    }

    [Fact]
    public void RefusesAChainThatLoops()
    {
        FatVolumeBuilder builder = new(1024 * 1024, "ESP", 1, s_timestamp) { Type = FatType.Fat12 };
        builder.AddFile("LOADER.EFI", s_loader);
        byte[] built = builder.Build();
        FatVolume intact = Open(built);
        uint first = intact.Find("LOADER.EFI")!.FirstCluster;

        // Cluster first points back at itself.
        int reserved = BinaryPrimitives.ReadUInt16LittleEndian(built.AsSpan(14));
        Span<byte> pair = built.AsSpan((reserved * 512) + (int)(first + (first / 2)), 2);
        uint word = BinaryPrimitives.ReadUInt16LittleEndian(pair);
        word = (first & 1) == 0 ? (word & 0xF000) | first : (word & 0x000F) | (first << 4);
        BinaryPrimitives.WriteUInt16LittleEndian(pair, (ushort)word);
        FatVolume damaged = Open(built);

        Assert.Throws<InvalidDataException>(() => damaged.ReadFile(damaged.Find("LOADER.EFI")!, MaxBytes));
    }

    [Fact]
    public void RefusesAFileLargerThanAsked()
    {
        FatVolume volume = Open(EspBuilder(1024 * 1024).Build());

        InvalidDataException refusal = Assert.Throws<InvalidDataException>(() => volume.ReadFile(volume.Find(@"EFI\BOOT\BOOTX64.EFI")!, 4096));

        Assert.Equal("BOOTX64.EFI holds 5000 bytes, more than the 4096 DDT reads.", refusal.Message);
    }

    [Fact]
    public void RefusesAPartitionWithoutAFileSystem()
    {
        Assert.Throws<InvalidDataException>(() => FatVolume.Open(new MemoryStream(new byte[1024 * 1024]), 0, 1024 * 1024));
        Assert.Throws<InvalidDataException>(() => Open(EspBuilder(1024 * 1024).Build()[..4096]));
    }

    [Fact]
    public void RefusesFilesThatDoNotFit()
    {
        FatVolumeBuilder builder = new(1024 * 1024, "ESP", 1, s_timestamp);
        builder.AddFile("BIG", new byte[2 * 1024 * 1024]);

        Assert.Throws<InvalidOperationException>(builder.Build);
        Assert.Throws<InvalidOperationException>(new FatVolumeBuilder(1024 * 1024, "cidata.old", 1, s_timestamp).Build);
        Assert.Throws<InvalidOperationException>(new FatVolumeBuilder(1024 * 1024, "ESP", 1, s_timestamp) { Type = FatType.Fat32 }.Build);
    }
}
