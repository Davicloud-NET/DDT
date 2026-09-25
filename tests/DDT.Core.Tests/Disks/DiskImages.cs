// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Disks;

namespace DDT.Core.Tests.Disks;

// Whole disks in memory, as a partitioning tool would leave them.
internal static class DiskImages
{
    public static byte[] Write(GptLayout layout, ReadOnlySpan<byte> sector0 = default)
    {
        byte[] disk = new byte[layout.DiskSectors * GptLayout.SectorSize];
        Place(disk, 0, layout.ProtectiveMbr(sector0));
        Place(disk, 1, layout.PrimaryHeader());
        Place(disk, layout.EntriesLba, layout.EntryArray());
        Place(disk, layout.BackupEntriesLba, layout.EntryArray());
        Place(disk, layout.BackupLba, layout.BackupHeader());

        return disk;
    }

    public static void Place(byte[] disk, long lba, ReadOnlySpan<byte> data) =>
        data.CopyTo(disk.AsSpan((int)(lba * GptLayout.SectorSize)));

    public static ReadOnlySpan<byte> Sector(byte[] disk, long lba) =>
        disk.AsSpan((int)(lba * GptLayout.SectorSize), GptLayout.SectorSize);
}
