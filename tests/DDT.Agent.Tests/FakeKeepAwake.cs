// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.InstalledPhase;

namespace DDT.Agent.Tests;

// Notes a hold and its release among the fake tools' calls. Never touches this PC's power.
internal sealed class FakeKeepAwake(FakeDeploymentTools tools) : IKeepAwake
{
    public int Held { get; private set; }

    public IDisposable Hold()
    {
        tools.Note("awake");
        Held++;

        return new Release(this, tools);
    }

    private sealed class Release(FakeKeepAwake owner, FakeDeploymentTools tools) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (!_released)
            {
                _released = true;
                owner.Held--;
                tools.Note("may sleep");
            }
        }
    }
}
