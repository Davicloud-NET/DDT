// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Disks;

// How FatVolumeBuilder lays out a volume, in sectors of 512 bytes.
internal sealed record FatGeometry(
    FatType Type,
    long TotalSectors,
    int SectorsPerCluster,
    int Reserved,
    int RootSectors,
    long FatSectors,
    uint Clusters);
