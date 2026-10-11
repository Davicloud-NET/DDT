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

    // firstSector is where the seed's partition starts on the disk. more go beside cloud-init's own files.
    public static byte[] Build(CloudInitSeedFiles files, uint serialNumber, DateTime timestamp, long firstSector, IReadOnlyList<SeedFile>? more = null) =>
        Volume(files, more, new FatVolumeBuilder(SizeBytes, Label, serialNumber, timestamp) { Type = FatType.Fat16, HiddenSectors = firstSector }).Build();

    // What the volume has left with these files on it, in whole clusters. Negative when they do not fit.
    public static long FreeBytes(CloudInitSeedFiles files, IReadOnlyList<SeedFile>? more = null) =>
        Volume(files, more, new FatVolumeBuilder(SizeBytes, Label, 0, DateTime.UnixEpoch) { Type = FatType.Fat16 }).FreeBytes();

    private static FatVolumeBuilder Volume(CloudInitSeedFiles files, IReadOnlyList<SeedFile>? more, FatVolumeBuilder volume)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(files.MetaData);
        ArgumentNullException.ThrowIfNull(files.UserData);

        volume.AddFile(MetaData, Encoding.UTF8.GetBytes(files.MetaData));
        volume.AddFile(UserData, Encoding.UTF8.GetBytes(files.UserData));

        if (files.NetworkConfig is not null)
        {
            volume.AddFile(NetworkConfig, Encoding.UTF8.GetBytes(files.NetworkConfig));
        }

        foreach (SeedFile file in more ?? [])
        {
            volume.AddFile(file.Path, file.Content);
        }

        return volume;
    }
}
