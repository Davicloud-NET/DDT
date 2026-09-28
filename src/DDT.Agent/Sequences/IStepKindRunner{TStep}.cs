// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// A runner for steps of type TStep that always run as the agent.
public interface IStepKindRunner<TStep> : IStepKindRunner
    where TStep : SequenceStep
{
    Task<StepResult> RunAsync(TStep step, StepContext context, CancellationToken cancellationToken);

    bool IStepKindRunner.Runs(SequenceStep step) => step is TStep;

    Task<StepResult> IStepKindRunner.RunAsync(SequenceStep step, StepContext context, IAccountSession? account, CancellationToken cancellationToken) =>
        RunAsync((TStep)step, context, cancellationToken);
}
