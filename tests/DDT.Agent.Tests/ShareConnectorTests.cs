// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class ShareConnectorTests
{
    private const string Password = "Pa55-never-logged";

    private readonly FakeNetworkConnections _network = new();
    private readonly StringWriter _console = new();
    private readonly AgentLog _log;

    public ShareConnectorTests() => _log = new AgentLog(new ImmediateTimeProvider(), _console);

    private ShareConnector Connector => new(_network, _log);

    [Fact]
    public async Task ConnectsTheShareOfAPathTemporarilyWithoutADriveLetterThenDisconnectsIt()
    {
        AgentShareConnection share = new(@"\\files.corp.example\drivers\Dell\5440", @"CORP\svc-drivers", Password);

        IAsyncDisposable connected = await Connector.ConnectAsync([share], null, TestContext.Current.CancellationToken);
        await connected.DisposeAsync();

        // The share of the path, not the folder in it.
        Assert.Equal([@"add \\files.corp.example\drivers as CORP\svc-drivers", @"cancel \\files.corp.example\drivers"], _network.Calls);
    }

    [Fact]
    public async Task DisconnectsEveryShareEvenWhenOneLater()
    {
        AgentShareConnection first = new(@"\\a.example\one", "user1", Password);
        AgentShareConnection second = new(@"\\b.example\two", "user2", Password);

        IAsyncDisposable connected = await Connector.ConnectAsync([first, second], null, TestContext.Current.CancellationToken);
        await connected.DisposeAsync();

        Assert.Equal(
            [@"add \\a.example\one as user1", @"add \\b.example\two as user2", @"cancel \\b.example\two", @"cancel \\a.example\one"],
            _network.Calls);
    }

    [Fact]
    public async Task CancelsAnOtherAccountsConnectionToTheSameServerFirstAndTriesOnce()
    {
        FakeNetworkConnections network = new(@"\\files.example\other");
        AgentShareConnection share = new(@"\\files.example\drivers", @"CORP\svc", Password);

        IAsyncDisposable connected = await new ShareConnector(network, _log).ConnectAsync([share], null, TestContext.Current.CancellationToken);
        await connected.DisposeAsync();

        Assert.Equal(
            [
                @"add \\files.example\drivers as CORP\svc",
                "enum",
                @"cancel \\files.example\other",
                @"add \\files.example\drivers as CORP\svc",
                @"cancel \\files.example\drivers",
            ],
            network.Calls);
    }

    [Fact]
    public async Task AShareThatCannotBeConnectedFailsTheStepNamingTheShareAndTheErrorWithoutThePassword()
    {
        _network.AddError = @"\\gone.example\share";
        AgentShareConnection share = new(@"\\gone.example\share", @"CORP\svc", Password);

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(
            () => new ShareConnector(_network, _log).ConnectAsync([share], null, TestContext.Current.CancellationToken));

        Assert.Contains(@"\\gone.example\share", exception.Message, StringComparison.Ordinal);
        Assert.Contains("error 67", exception.Message, StringComparison.Ordinal);
        Assert.Contains("The network name cannot be found.", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisconnectsWhatConnectedWhenALaterShareFails()
    {
        _network.AddError = @"\\b.example\two";
        AgentShareConnection first = new(@"\\a.example\one", "user1", Password);
        AgentShareConnection second = new(@"\\b.example\two", "user2", Password);

        await Assert.ThrowsAsync<DeploymentStepException>(
            () => Connector.ConnectAsync([first, second], null, TestContext.Current.CancellationToken));

        Assert.Equal([@"add \\a.example\one as user1", @"add \\b.example\two as user2", @"cancel \\a.example\one"], _network.Calls);
    }

    [Fact]
    public async Task ReusesTheSameConnectionForASecondShareOnTheSameServerWithTheSameAccount()
    {
        AgentShareConnection drivers = new(@"\\files.example\drivers", @"CORP\svc", Password);
        AgentShareConnection apps = new(@"\\files.example\apps", @"CORP\svc", Password);

        IAsyncDisposable connected = await Connector.ConnectAsync([drivers, apps], null, TestContext.Current.CancellationToken);
        await connected.DisposeAsync();

        // Both connect; Windows allows a second share on a server the account already reached.
        Assert.Equal(
            [
                @"add \\files.example\drivers as CORP\svc",
                @"add \\files.example\apps as CORP\svc",
                @"cancel \\files.example\apps",
                @"cancel \\files.example\drivers",
            ],
            _network.Calls);
    }

    [Fact]
    public async Task NeverLogsThePassword()
    {
        FakeNetworkConnections network = new(@"\\files.example\other");
        AgentShareConnection share = new(@"\\files.example\drivers", @"CORP\svc", Password);

        IAsyncDisposable connected = await new ShareConnector(network, _log).ConnectAsync([share], null, TestContext.Current.CancellationToken);
        await connected.DisposeAsync();

        Assert.DoesNotContain(Password, _console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectsInsideTheAccountsLogonSessionWhenGivenOne()
    {
        FakeAccountTools accounts = new();
        IAccountSession account = await accounts.Logons.LogOnAsync(new AgentAccount(@"CORP\svc", Password), TestContext.Current.CancellationToken);
        AgentShareConnection share = new(@"\\files.example\drivers", @"CORP\svc", Password);

        IAsyncDisposable connected = await Connector.ConnectAsync([share], account, TestContext.Current.CancellationToken);
        await connected.DisposeAsync();

        // The connect and the disconnect both run while acting as the account, so the share belongs to its session.
        Assert.Equal(
            [@"sign in CORP\svc", @"impersonate CORP\svc", @"impersonate CORP\svc"],
            accounts.Events);
    }

    [Theory]
    [InlineData(@"\\files.example\drivers\Dell", @"\\files.example\drivers")]
    [InlineData(@"\\files.example\drivers", @"\\files.example\drivers")]
    [InlineData(@"\\files.example\drivers\", @"\\files.example\drivers")]
    public void ReducesAPathToItsShare(string path, string share) => Assert.Equal(share, ShareConnector.RemoteName(path));

    [Theory]
    [InlineData(@"\\host")]
    [InlineData(@"\\host\")]
    [InlineData("host\\share")]
    [InlineData(@"C:\folder")]
    public void RefusesWhatIsNotAShare(string path) =>
        Assert.Throws<DeploymentStepException>(() => ShareConnector.RemoteName(path));
}
