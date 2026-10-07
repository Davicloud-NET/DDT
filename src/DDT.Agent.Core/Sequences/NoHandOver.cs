// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Sequences;

// For a boot environment whose runs end in it, as the Linux boot environment's do.
public sealed class NoHandOver : IHandOver
{
    public const string Message = "This boot environment hands no run over to an installed system, so the run cannot go on there.";

    Task IHandOver.HandOverAsync(SequenceRun run, RunBootOrder bootOrder, CancellationToken cancellationToken) =>
        Task.FromException(new DeploymentStepException(Message));
}
