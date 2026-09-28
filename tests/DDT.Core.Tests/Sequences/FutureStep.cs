// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Tests.Sequences;

// A step kind this version doesn't have, which erases the disk.
public sealed record FutureStep : SequenceStep
{
    public override SequencePhase? RequiredPhase => SequencePhase.WindowsPE;

    public override bool ErasesDisk => true;
}
