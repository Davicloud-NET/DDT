// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Applications;
using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class DeployAccountElevationTests : IDisposable
{
    private readonly List<string> _events = [];
    private readonly FakeDeployAccount _account;
    private readonly StringWriter _output = new();
    private readonly AgentLog _log;

    public DeployAccountElevationTests()
    {
        _account = new FakeDeployAccount(_events);
        _log = new AgentLog(TimeProvider.System, _output);
    }

    public void Dispose() => _output.Dispose();

    [Fact]
    public async Task TheAccountJoinsAdministratorsBeforeItsBatchLogonAndLeavesAfterItSignedOut()
    {
        using (DeployAccountElevation raised = await DeployAccountElevation.RaiseAsync(_account, _log, TestContext.Current.CancellationToken))
        {
            Assert.True(_account.IsAdministrator);
            Assert.Equal("DDTDeploy", raised.Session.UserName);
        }

        Assert.Equal(["join Administrators", "batch logon", "sign out batch", "leave Administrators"], _events);
        Assert.False(_account.IsAdministrator);
        Assert.Contains("DDTDeploy is a standard account again.", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusedBatchLogonTakesTheAccountOutOfAdministratorsAgain()
    {
        _account.LogOnFailure = new DeploymentStepException("DDTDeploy could not sign in on this computer (1385).");

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(
            () => DeployAccountElevation.RaiseAsync(_account, _log, TestContext.Current.CancellationToken));

        Assert.Equal("DDTDeploy could not sign in on this computer (1385).", exception.Message);
        Assert.Equal(["join Administrators", "batch logon", "leave Administrators"], _events);
        Assert.False(_account.IsAdministrator);
    }

    [Fact]
    public async Task AMembershipThatCannotBeRemovedIsAWarningForTheNextStart()
    {
        _account.LeaveFailure = new DeploymentStepException("The account DDTDeploy could not be taken out of Administrators (error 5).");

        using (await DeployAccountElevation.RaiseAsync(_account, _log, TestContext.Current.CancellationToken))
        {
        }

        Assert.Contains(
            "The account DDTDeploy could not be taken out of Administrators (error 5). The agent tries again when it starts next.",
            _output.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisposingTwiceLowersOnce()
    {
        DeployAccountElevation raised = await DeployAccountElevation.RaiseAsync(_account, _log, TestContext.Current.CancellationToken);

        raised.Dispose();
        raised.Dispose();

        Assert.Single(_events, "leave Administrators");
    }
}
