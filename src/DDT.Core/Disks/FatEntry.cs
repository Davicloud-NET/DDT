// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Disks;

// A file or directory in a FAT volume. Name is its long name when it has one, else its short name.
public sealed record FatEntry(string Name, bool IsDirectory, long Size, uint FirstCluster);
