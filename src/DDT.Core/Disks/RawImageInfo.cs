// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Disks;

// What a raw disk image holds that DDT needs before it stores the image: its partition table, its EFI system
// partition, and the files there that firmware starts. BootProblem says why no boot file could be read, if so.
public sealed record RawImageInfo(
    long SizeBytes,
    GptLayout Table,
    GptPartition? SystemPartition,
    IReadOnlyList<RawImageBootFile> BootFiles,
    string? BootProblem)
{
    // The smallest disk the image can be written to: all of it, in whole sectors, and after its last partition room
    // for the backup table DDT writes at the disk's end.
    public long MinimumDiskBytes =>
        Math.Max(
            (SizeBytes + GptLayout.SectorSize - 1) / GptLayout.SectorSize * GptLayout.SectorSize,
            (Table.LastUsedLba + 1 + Table.EntrySectors + 1) * GptLayout.SectorSize);
}
