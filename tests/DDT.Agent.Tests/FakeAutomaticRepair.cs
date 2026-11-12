// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.InstalledPhase;

namespace DDT.Agent.Tests;

// Notes each turn among the fake tools' calls, so a test sees it in order with the rest.
internal sealed class FakeAutomaticRepair(FakeDeploymentTools tools) : IAutomaticRepair
{
    public Task TurnOffAsync(CancellationToken cancellationToken)
    {
        tools.Note("automatic repair off");

        return Task.CompletedTask;
    }

    public Task PutBackAsync(CancellationToken cancellationToken)
    {
        tools.Note("automatic repair back");

        return Task.CompletedTask;
    }
}
