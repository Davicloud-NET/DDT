// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// Where the EFI system partition lies on its GPT disk, in the terms of a UEFI hard drive device path: the partition's
// entry number, its first block and length in logical blocks, and its unique partition GUID.
public sealed record EspPartition(uint PartitionNumber, ulong StartLba, ulong SizeLba, Guid PartitionId);
