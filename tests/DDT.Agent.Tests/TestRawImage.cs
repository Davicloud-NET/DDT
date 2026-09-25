// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Images;
using DDT.Core.Disks;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace DDT.Agent.Tests;

// A raw disk image of 8 MiB as a distribution lays its cloud image out, and the file the server stores for it: an EFI
// system partition at 1 MiB with \EFI\BOOT\BOOTX64.EFI, and a root partition whose first 2 MiB are random, with the
// backup table at the image's end. tail adds bytes that are not a whole sector.
internal sealed class TestRawImage
{
    public const long Sectors = 16384;
    public const int EspNumber = 1;
    public const long EspFirst = 2048;
    public const long EspSectors = 4096;

    public static readonly Guid ImageId = Guid.Parse("0193a4b2-0000-7000-8000-00000000d001");
    public static readonly Guid EspId = Guid.Parse("0193a4b2-0000-4000-8000-00000000d0e5");

    public TestRawImage(int seed = 1, int tail = 0)
    {
        Random random = new(seed);
        Layout = GptLayout.Create(Sectors, Guid.Parse("0193a4b2-0000-4000-8000-00000000d0d1"))
            .WithPartition(GptPartitionTypes.EfiSystem, EspId, "EFI", EspFirst, EspFirst + EspSectors - 1)
            .WithPartition(GptPartitionTypes.LinuxFileSystem, Guid.Parse("0193a4b2-0000-4000-8000-00000000d0f5"), "root", EspFirst + EspSectors, Sectors - 2048);

        byte[] disk = new byte[(Sectors * GptLayout.SectorSize) + tail];
        Place(disk, 0, Layout.ProtectiveMbr(default));
        Place(disk, 1, Layout.PrimaryHeader());
        Place(disk, Layout.EntriesLba, Layout.EntryArray());
        Place(disk, Layout.BackupEntriesLba, Layout.EntryArray());
        Place(disk, Layout.BackupLba, Layout.BackupHeader());

        FatVolumeBuilder esp = new(EspSectors * GptLayout.SectorSize, "ESP", 1, new DateTime(2026, 9, 1)) { HiddenSectors = EspFirst };
        esp.AddFile(@"EFI\BOOT\BOOTX64.EFI", [0x4D, 0x5A, 1, 2, 3]);
        Place(disk, EspFirst, esp.Build());
        random.NextBytes(disk.AsSpan((int)((EspFirst + EspSectors) * GptLayout.SectorSize), 2 * 1024 * 1024));
        random.NextBytes(disk.AsSpan(disk.Length - tail));

        Disk = disk;

        using Compressor compressor = new(9);
        compressor.SetParameter(ZSTD_cParameter.ZSTD_c_checksumFlag, 1);
        Compressed = compressor.Wrap(disk).ToArray();
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(Compressed));
    }

    public GptLayout Layout { get; }

    public byte[] Disk { get; }

    public byte[] Compressed { get; }

    public string Sha256 { get; }

    public AgentRunImage RunImage(ImageBootCapability capability = ImageBootCapability.SecureBootOk) =>
        new(ImageId, "noble-test", Sha256, Compressed.Length, 0, Disk.Length, ImageKind.RawDisk, capability);

    public ScriptedAgentServer Serve(ScriptedAgentServer server)
    {
        ArgumentNullException.ThrowIfNull(server);

        return server.ServeFile(Sha256, Compressed);
    }

    private static void Place(byte[] disk, long lba, ReadOnlySpan<byte> data) =>
        data.CopyTo(disk.AsSpan((int)(lba * GptLayout.SectorSize)));
}
