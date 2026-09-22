// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// Root directories of the new partitions, such as S:\, W:\ and R:\, and the unique GUIDs of the EFI system partitions
// the partitioning erased, which old firmware boot entries may still name. The partition ids are the unique GUIDs of
// the new partitions, which find them again after a restart; Guid.Empty where they were not read.
public sealed record TargetVolumes(string System, string Windows, string Recovery, IReadOnlyList<Guid> ErasedSystemPartitionIds)
{
    public Guid SystemPartitionId { get; init; }

    public Guid WindowsPartitionId { get; init; }

    public Guid RecoveryPartitionId { get; init; }
}
