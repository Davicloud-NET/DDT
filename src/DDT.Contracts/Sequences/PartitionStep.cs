// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// Microsoft's UEFI layout: EFI system, MSR, Windows, and a recovery partition after Windows. The defaults are the
// sizes DDT used before task sequences.
public sealed record PartitionStep : SequenceStep
{
    public int SystemPartitionMegabytes { get; init; } = 300;

    public int RecoveryPartitionMegabytes { get; init; } = 1024;

    [JsonIgnore]
    public override SequencePhase? RequiredPhase => SequencePhase.WindowsPE;

    [JsonIgnore]
    public override bool ErasesDisk => true;
}
