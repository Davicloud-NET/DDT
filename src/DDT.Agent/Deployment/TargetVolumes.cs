// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// The new partitions' roots, such as S:\, and the EFI system partitions the partitioning erased, which old firmware boot
// entries may still name. The partition ids find the new ones again after a restart; Guid.Empty where not read.
public sealed record TargetVolumes(string System, string Windows, string Recovery, IReadOnlyList<Guid> ErasedSystemPartitionIds)
{
    public Guid SystemPartitionId { get; init; }

    public Guid WindowsPartitionId { get; init; }

    public Guid RecoveryPartitionId { get; init; }
}
