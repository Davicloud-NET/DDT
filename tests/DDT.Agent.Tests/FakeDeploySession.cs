// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.WindowsPhase;

namespace DDT.Agent.Tests;

// Records what the loop asks of DDT's session among the tools' calls. ends says whether an end completes, because a
// stop may end the wait for a sign-out.
internal sealed class FakeDeploySession(FakeDeploymentTools tools, bool ends = true) : IDeploySession
{
    public Task PrepareAsync(CancellationToken cancellationToken)
    {
        tools.Note("prepare the session");

        return Task.CompletedTask;
    }

    public void SetupFinished() => tools.Note("the session takes over the sign-in");

    public Task<bool> EndAsync(bool signOut, CancellationToken cancellationToken)
    {
        tools.Note(signOut ? "end the session, signing out" : "end the session once someone signed out");

        return Task.FromResult(ends);
    }
}
