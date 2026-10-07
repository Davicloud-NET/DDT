// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;

namespace DDT.Agent.Tests;

// Stands in for a system's hand-over, which only goes into the tools' journal.
internal sealed class FakeHandOver(FakeDeploymentTools tools) : IHandOver
{
    Task IHandOver.HandOverAsync(SequenceRun run, RunBootOrder bootOrder, CancellationToken cancellationToken)
    {
        tools.Note("hand over");

        return Task.CompletedTask;
    }
}
