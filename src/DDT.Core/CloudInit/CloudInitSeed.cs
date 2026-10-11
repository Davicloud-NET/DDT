// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Core.Disks;

namespace DDT.Core.CloudInit;

// cloud-init's NoCloud data source looks for its seed on any file system labelled CIDATA. It reads user-data,
// meta-data, network-config and vendor-data from the root. The seed is a FAT volume on a separate partition at the end
// of the disk.
public static class CloudInitSeed
{
    public const string Label = "CIDATA";
    public const string UserData = "user-data";
    public const string MetaData = "meta-data";
    public const string NetworkConfig = "network-config";

    // The administrator has no field for it. DDT puts its own there when a run goes on in the installed Linux.
    public const string VendorData = "vendor-data";

    // Far more than a seed needs, and still FAT16, which every Linux reads.
    public const long SizeBytes = 64L * 1024 * 1024;

    // The disk space the seed needs beyond the image. That's the seed itself, a mebibyte to align its start, and the
    // backup partition table after it.
    public const long DiskBytes = SizeBytes + (2L * 1024 * 1024);

    // A seed step's files as they go onto the volume, in that order.
    public static IReadOnlyList<SeedFile> FilesOf(CloudInitSeedFiles files)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(files.MetaData);
        ArgumentNullException.ThrowIfNull(files.UserData);

        List<SeedFile> list = [new(MetaData, Encoding.UTF8.GetBytes(files.MetaData)), new(UserData, Encoding.UTF8.GetBytes(files.UserData))];

        if (files.NetworkConfig is not null)
        {
            list.Add(new SeedFile(NetworkConfig, Encoding.UTF8.GetBytes(files.NetworkConfig)));
        }

        return list;
    }

    // firstSector is where the seed's partition starts on the disk.
    public static byte[] Build(CloudInitSeedFiles files, uint serialNumber, DateTime timestamp, long firstSector) =>
        Build(FilesOf(files), serialNumber, timestamp, firstSector);

    // The same with any files, one after another, such as a seed's own and then the agent's.
    public static byte[] Build(IReadOnlyList<SeedFile> files, uint serialNumber, DateTime timestamp, long firstSector) =>
        Volume(files, new FatVolumeBuilder(SizeBytes, Label, serialNumber, timestamp) { Type = FatType.Fat16, HiddenSectors = firstSector }).Build();

    // What the volume has left with these files on it, in whole clusters. Negative when they do not fit.
    public static long FreeBytes(IReadOnlyList<SeedFile> files) =>
        Volume(files, new FatVolumeBuilder(SizeBytes, Label, 0, DateTime.UnixEpoch) { Type = FatType.Fat16 }).FreeBytes();

    private static FatVolumeBuilder Volume(IReadOnlyList<SeedFile> files, FatVolumeBuilder volume)
    {
        ArgumentNullException.ThrowIfNull(files);

        foreach (SeedFile file in files)
        {
            volume.AddFile(file.Path, file.Content);
        }

        return volume;
    }
}
