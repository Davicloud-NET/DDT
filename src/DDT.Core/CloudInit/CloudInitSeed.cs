// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Core.Disks;

namespace DDT.Core.CloudInit;

// cloud-init's NoCloud data source finds its seed on any file system labelled CIDATA, and reads user-data, meta-data
// and network-config from its root. The seed is a FAT volume of its own partition at the end of the disk.
public static class CloudInitSeed
{
    public const string Label = "CIDATA";
    public const string UserData = "user-data";
    public const string MetaData = "meta-data";
    public const string NetworkConfig = "network-config";

    // Far more than seeds need, and still FAT16, which every Linux reads.
    public const long SizeBytes = 64L * 1024 * 1024;

    // The rendered files; networkConfig is left out when null. firstSector is where the partition starts on the disk.
    public static byte[] Build(string metaData, string userData, string? networkConfig, uint serialNumber, DateTime timestamp, long firstSector)
    {
        ArgumentNullException.ThrowIfNull(metaData);
        ArgumentNullException.ThrowIfNull(userData);

        FatVolumeBuilder volume = new(SizeBytes, Label, serialNumber, timestamp) { Type = FatType.Fat16, HiddenSectors = firstSector };
        volume.AddFile(MetaData, Encoding.UTF8.GetBytes(metaData));
        volume.AddFile(UserData, Encoding.UTF8.GetBytes(userData));

        if (networkConfig is not null)
        {
            volume.AddFile(NetworkConfig, Encoding.UTF8.GetBytes(networkConfig));
        }

        return volume.Build();
    }
}
