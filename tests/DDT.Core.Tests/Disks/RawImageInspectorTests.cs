// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Disks;
using Xunit;

namespace DDT.Core.Tests.Disks;

public sealed class RawImageInspectorTests
{
    // 16 MiB: an EFI system partition of 4 MiB at 1 MiB, and a root file system after it.
    private const long DiskSectors = 32768;
    private const long EspFirst = 2048;
    private const long EspSectors = 8192;

    private static readonly byte[] s_fallback = [.. "MZ fallback"u8];
    private static readonly byte[] s_shim = [.. "MZ shim"u8];

    private static GptLayout Layout(Guid espType) =>
        GptLayout.Create(DiskSectors, Guid.NewGuid())
            .WithPartition(espType, Guid.NewGuid(), "EFI", EspFirst, EspFirst + EspSectors - 1)
            .WithPartition(GptPartitionTypes.LinuxFileSystem, Guid.NewGuid(), "root", EspFirst + EspSectors, DiskSectors - 2048);

    private static byte[] Image(Action<FatVolumeBuilder>? files = null, Guid? espType = null)
    {
        byte[] disk = DiskImages.Write(Layout(espType ?? GptPartitionTypes.EfiSystem));
        FatVolumeBuilder esp = new(EspSectors * GptLayout.SectorSize, "ESP", 7, new DateTime(2026, 1, 1)) { HiddenSectors = EspFirst };
        files?.Invoke(esp);
        DiskImages.Place(disk, EspFirst, esp.Build());

        return disk;
    }

    private static RawImageInfo Inspect(byte[] image) => RawImageInspector.Inspect(new MemoryStream(image));

    [Fact]
    public void ReadsTheFallbackFileAndTheShimsBesideTheBootLoaders()
    {
        RawImageInfo info = Inspect(Image(esp =>
        {
            esp.AddFile(@"EFI\BOOT\BOOTX64.EFI", s_fallback);
            esp.AddFile(@"EFI\BOOT\fbx64.efi", [1]);
            esp.AddFile(@"EFI\debian\shimx64.efi", s_shim);
            esp.AddFile(@"EFI\debian\grubx64.efi", [2]);
        }));

        Assert.Null(info.BootProblem);
        Assert.Equal(EspFirst, info.SystemPartition!.FirstLba);
        Assert.Equal(DiskSectors * GptLayout.SectorSize, info.SizeBytes);
        Assert.Equal(DiskSectors * GptLayout.SectorSize, info.MinimumDiskBytes);
        Assert.Equal([@"\EFI\BOOT\BOOTX64.EFI", @"\EFI\debian\shimx64.efi"], info.BootFiles.Select(file => file.Path));
        Assert.Equal(s_fallback, info.BootFiles[0].Content);
        Assert.Equal(s_shim, info.BootFiles[1].Content);
    }

    [Fact]
    public void SaysWhenTheImageHasNoEfiSystemPartition()
    {
        RawImageInfo info = Inspect(Image(espType: GptPartitionTypes.BasicData));

        Assert.Null(info.SystemPartition);
        Assert.Empty(info.BootFiles);
        Assert.Equal("The image has no EFI system partition.", info.BootProblem);
    }

    [Fact]
    public void SaysWhenTheEfiSystemPartitionCannotBeRead()
    {
        byte[] image = Image();
        DiskImages.Place(image, EspFirst, new byte[GptLayout.SectorSize]);

        RawImageInfo info = Inspect(image);

        Assert.NotNull(info.SystemPartition);
        Assert.StartsWith("The image's EFI system partition cannot be read: ", info.BootProblem, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsAnEmptyEfiSystemPartition()
    {
        RawImageInfo info = Inspect(Image());

        Assert.Empty(info.BootFiles);
        Assert.Null(info.BootProblem);
    }

    [Fact]
    public void RefusesAnImageThatEndsBeforeItsPartitions()
    {
        byte[] image = Image();

        InvalidGptException refusal = Assert.Throws<InvalidGptException>(() => Inspect(image[..(int)(DiskSectors * GptLayout.SectorSize / 2)]));

        Assert.EndsWith("The file is incomplete.", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAFileThatIsNoDiskImage()
    {
        Assert.Equal(GptLayout.NoTableMessage, Assert.Throws<InvalidGptException>(() => Inspect(new byte[1024 * 1024])).Message);
        Assert.Equal(GptLayout.NoTableMessage, Assert.Throws<InvalidGptException>(() => Inspect(new byte[100])).Message);
    }

    [Fact]
    public void NeedsRoomForTheBackupTableAfterTheLastPartition()
    {
        GptLayout layout = GptLayout.Create(8192, Guid.NewGuid()).WithPartition(GptPartitionTypes.EfiSystem, Guid.NewGuid(), "EFI", 2048, 8158);
        byte[] image = DiskImages.Write(layout);

        // The image ends right after its last partition, without the backup table it had there.
        RawImageInfo info = Inspect(image[..(8159 * GptLayout.SectorSize + 100)]);

        Assert.Equal((8159 + 33) * GptLayout.SectorSize, info.MinimumDiskBytes);
    }
}
