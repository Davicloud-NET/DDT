// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Core.Tests.Sequences;

// Runs each step by the script given for it and records every run. A step without a script is done.
public sealed class ScriptedStepRunner : IStepRunner
{
    private readonly Dictionary<Guid, Func<StepContext, CancellationToken, Task<StepResult>>> _scripts = [];
    private readonly List<(SequenceStep Step, StepContext Context)> _runs = [];

    public IReadOnlyList<(SequenceStep Step, StepContext Context)> Runs => _runs;

    public IReadOnlyList<Guid> Ran => [.. _runs.Select(run => run.Step.Id)];

    public ScriptedStepRunner On(SequenceStep step, StepResult result) => On(step, (_, _) => Task.FromResult(result));

    public ScriptedStepRunner On(SequenceStep step, Func<StepContext, CancellationToken, Task<StepResult>> script)
    {
        _scripts[step.Id] = script;

        return this;
    }

    public StepContext ContextOf(SequenceStep step) => _runs.Single(run => run.Step.Id == step.Id).Context;

    public Task<StepResult> RunAsync(SequenceStep step, StepContext context, CancellationToken cancellationToken)
    {
        _runs.Add((step, context));

        return _scripts.TryGetValue(step.Id, out Func<StepContext, CancellationToken, Task<StepResult>>? script)
            ? script(context, cancellationToken)
            : Task.FromResult(StepResult.Done());
    }
}
