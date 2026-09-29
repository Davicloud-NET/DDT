// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// The unique GUIDs of a run's system, Windows and recovery partitions, and of the EFI system partitions its
// partitioning erased. They find the partitions again after a restart, which leaves them without letters.
public sealed record RunDiskIds(Guid System, Guid Windows, Guid Recovery, IReadOnlyList<Guid> ErasedSystemPartitionIds);
