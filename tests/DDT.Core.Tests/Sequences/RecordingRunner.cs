// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Core.Tests.Sequences;

// Each step's result comes from its context alone, so a resumed run does what the first one did. Mark is the blob
// saved as the step started: its Running mark, or the blob a resumable step was found Running in.
internal sealed class RecordingRunner(IReadOnlyDictionary<Guid, Func<StepContext, StepResult>> behaviours, BlobStore store) : IStepRunner
{
    private readonly List<(Guid StepId, int Mark, StepContext Context)> _runs = [];

    public IReadOnlyList<(Guid StepId, int Mark, StepContext Context)> Runs => _runs;

    public IReadOnlyList<Guid> Ran => [.. _runs.Select(run => run.StepId)];

    public Task<StepResult> RunAsync(SequenceStep step, StepContext context, CancellationToken cancellationToken)
    {
        _runs.Add((step.Id, store.Blobs.Count - 1, context));

        return Task.FromResult(behaviours.TryGetValue(step.Id, out Func<StepContext, StepResult>? behaviour)
            ? behaviour(context)
            : step is RebootStep ? StepResult.RebootRequired() : StepResult.Done());
    }
}
