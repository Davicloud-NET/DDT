// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Sequences;

// The runners for the kinds of step a platform runs.
public interface IStepRunners
{
    // Each run gets its own, because they share its session, store and heartbeat.
    IReadOnlyList<IStepKindRunner> For(StepRunnerParts run);
}
