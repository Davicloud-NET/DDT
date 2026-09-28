// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Disks;

// A raw disk image's partition table, its EFI system partition and the files there that firmware starts. BootProblem
// says why boot files could not be read; UnreadableBootFiles names those that are there but unreadable.
public sealed record RawImageInfo(
    long SizeBytes,
    GptLayout Table,
    GptPartition? SystemPartition,
    IReadOnlyList<RawImageBootFile> BootFiles,
    string? BootProblem,
    IReadOnlyList<string>? UnreadableBootFiles = null)
{
    // The smallest disk the image can be written to: all of it, in whole sectors, and after its last partition room
    // for the backup table DDT writes at the disk's end.
    public long MinimumDiskBytes =>
        Math.Max(
            (SizeBytes + GptLayout.SectorSize - 1) / GptLayout.SectorSize * GptLayout.SectorSize,
            (Table.LastUsedLba + 1 + Table.EntrySectors + 1) * GptLayout.SectorSize);
}
