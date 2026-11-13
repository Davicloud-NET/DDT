// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.InstalledPhase;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class FirstLogonTextsTests
{
    [Theory]
    [InlineData(1, "1 first-logon command and the automatic logon of Admin, 1 time")]
    [InlineData(2, "1 first-logon command and the automatic logon of Admin, 2 times")]
    public void CountsTheLogonsInWords(int count, string expected)
    {
        AgentFirstLogon firstLogon = new(["echo one"], new AgentAutoLogon("Admin", null, "secret", count));

        Assert.Equal(expected, FirstLogonTexts.Summary(firstLogon));
    }
}
