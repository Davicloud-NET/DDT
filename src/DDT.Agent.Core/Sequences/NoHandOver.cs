// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Sequences;

// The hand-over of an agent that runs in the installed system already, where a run has nowhere left to go.
public sealed class NoHandOver : IHandOver
{
    public const string Message = "The run is in the installed system already, so it cannot be handed over to it.";

    Task IHandOver.HandOverAsync(SequenceRun run, RunBootOrder bootOrder, CancellationToken cancellationToken) =>
        Task.FromException(new DeploymentStepException(Message));

    void IHandOver.Withdraw(SequenceRun run)
    {
    }
}
