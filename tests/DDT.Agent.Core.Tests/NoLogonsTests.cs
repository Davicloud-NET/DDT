// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class NoLogonsTests
{
    // The validator lets no such step into a boot environment. Should one arrive, it fails with a sentence.
    [Fact]
    public async Task RefusesAStepThatRunsAsAnAccountWithoutNamingItsPassword()
    {
        AgentAccount account = new(@"EXAMPLE\setup", "not for any log");

        DeploymentStepException refusal = await Assert.ThrowsAsync<DeploymentStepException>(
            () => new NoLogons().LogOnAsync(account, TestContext.Current.CancellationToken));

        Assert.Equal("This system runs nothing as an account, so a step that names one cannot run in it.", refusal.Message);
    }
}
