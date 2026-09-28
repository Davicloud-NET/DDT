// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Disks;

// Number is the entry's position in the table, counted from 1. Windows and Linux number the partition the same way.
public sealed record GptPartition(int Number, Guid Type, Guid Id, long FirstLba, long LastLba, ulong Attributes, string Name)
{
    public long Sectors => LastLba - FirstLba + 1;
}
