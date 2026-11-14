// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.InstalledPhase;
using DDT.Contracts.Agents;

namespace DDT.Agent.Tests;

// Notes the set-up among the fake tools' calls, and keeps what it was given. Fails with Failure when set.
internal sealed class FakeFirstLogon(FakeDeploymentTools tools) : IFirstLogon
{
    public List<AgentFirstLogon> SetUps { get; } = [];

    public Exception? Failure { get; init; }

    public void SetUp(AgentFirstLogon firstLogon)
    {
        tools.Note("first logon");

        if (Failure is not null)
        {
            throw Failure;
        }

        SetUps.Add(firstLogon);
    }
}
