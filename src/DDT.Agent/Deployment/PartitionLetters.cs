// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// The drive letters of the EFI system, Windows and recovery partitions while the agent works on them.
public sealed record PartitionLetters(char System, char Windows, char Recovery);
