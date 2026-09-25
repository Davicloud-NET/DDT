// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Tests.Sequences;

// A kind this version does not have, which erases the disk like the raw image step M6 added.
public sealed record FutureStep : SequenceStep
{
    public override SequencePhase? RequiredPhase => SequencePhase.WindowsPE;

    public override bool ErasesDisk => true;
}
