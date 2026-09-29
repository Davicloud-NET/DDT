// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Disks;

// What a GPT header says about the disk and its table. The header's own LBA and its CRCs are written fresh each time.
internal sealed record GptHeaderFields(
    Guid DiskId,
    long FirstUsableLba,
    long LastUsableLba,
    long BackupLba,
    long EntriesLba,
    int EntryCount,
    int EntrySize);
