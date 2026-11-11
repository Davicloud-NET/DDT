// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Tests.Sequences;

// A step kind this version doesn't have, which may restart the machine and go on.
public sealed record ContinuingStep : SequenceStep
{
    public int Limit { get; init; } = 3;

    public bool TakesUp { get; init; }

    public SequencePhase Phase { get; init; } = SequencePhase.Windows;

    public override SequencePhase? RequiredPhase => Phase;

    public override int RestartLimit => Limit;

    public override bool Resumable => TakesUp;

    // Like every kind after version 2, it runs as a tree, whose state keeps passes.
    public override int MinimumVersion => 3;
}
