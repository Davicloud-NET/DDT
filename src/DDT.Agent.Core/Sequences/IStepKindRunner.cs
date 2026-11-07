// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Runs the steps of one kind for AgentStepRunner, which asks each runner in turn whether a step is its kind.
public interface IStepKindRunner
{
    bool Runs(SequenceStep step);

    Task<StepResult> RunAsync(SequenceStep step, StepContext context, StepResources resources, CancellationToken cancellationToken);
}
