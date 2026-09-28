// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// One runner for every leaf kind, so a new kind needs no change to the engine; containers and Set variable never reach
// it. An exception fails the step, and once a stop is requested any failure counts as the stop.
public interface IStepRunner
{
    Task<StepResult> RunAsync(SequenceStep step, StepContext context, CancellationToken cancellationToken);
}
