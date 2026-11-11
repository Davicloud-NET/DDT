// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Applications;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Applications;
using Xunit;
using static DDT.Agent.Tests.TestApplications;

namespace DDT.Agent.Tests;

public sealed class WingetApplicationInstallerTests : IDisposable
{

    private static readonly AgentApplication s_sevenZip = Tool with { Kind = ApplicationKind.Winget, Command = null, WingetId = "7zip.7zip" };

    private readonly List<string> _events = [];
    private readonly RecordingToolRunner _tools = new();
    private readonly ImmediateTimeProvider _time = new();
    private readonly FakeDeployAccount _account;
    private readonly string _profile = Directory.CreateTempSubdirectory("ddt-profile-").FullName;

    public WingetApplicationInstallerTests()
    {
        _account = new FakeDeployAccount(_events) { ProfilePath = _profile };
        _tools.AnswerExitCode = (file, _, options) =>
        {
            _events.Add($"run {Path.GetFileName(file)} as {options.Account?.UserName}");

            return 0;
        };
    }

    private string Alias => WingetApplicationInstaller.AliasIn(_profile);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_profile, recursive: true);

    private Task<int> InstallAsync(AgentApplication application, CancellationToken cancellationToken) =>
        new WingetApplicationInstaller(_tools, _account, new AgentLog(TimeProvider.System, TextWriter.Null), _time).InstallAsync(
            new ApplicationLaunch(application, @"C:\DDT\scripts", @"C:\DDT\scripts", @"C:\DDT\logs", new Dictionary<string, string> { ["DDT_APPLICATION"] = "7-Zip" }),
            cancellationToken);

    [Fact]
    public async Task WingetInstallsForTheMachineAsTheAccountRaisedOnlyWhileItRuns()
    {
        int exitCode = await InstallAsync(s_sevenZip, Cancellation);

        Assert.Equal(0, exitCode);
        Assert.Equal(
            [RecordingToolRunner.CommandLine(
                Alias,
                "install",
                "--id",
                "7zip.7zip",
                "--exact",
                "--source",
                "winget",
                "--scope",
                "machine",
                "--silent",
                "--accept-source-agreements",
                "--accept-package-agreements",
                "--disable-interactivity")],
            _tools.Calls);
        Assert.Equal(["join Administrators", "batch logon", "run winget.exe as DDTDeploy", "sign out batch", "leave Administrators"], _events);
        ToolRunOptions options = Assert.Single(_tools.Options);
        Assert.Equal((_profile, TimeSpan.FromMinutes(5), "7-Zip"), (options.WorkingDirectory, options.Timeout, options.Environment?["DDT_APPLICATION"]));
        Assert.True(options.WithoutHandleList);
        Assert.NotNull(options.Lines);
        Assert.Null(options.Lines("\u2588\u2588\u2588\u2592\u2592  1.50 MB / 3.00 MB"));
    }

    [Fact]
    public async Task AVersionTheApplicationNamesIsTheOneInstalled()
    {
        await InstallAsync(s_sevenZip with { WingetId = " 7zip.7zip ", WingetVersion = " 24.09 " }, Cancellation);

        Assert.Contains("install --id 7zip.7zip --exact --version 24.09 --source winget", Assert.Single(_tools.Calls), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutDdtsSessionWingetCannotRunAndNothingIsRaised()
    {
        _account.Session = null;

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(() => InstallAsync(s_sevenZip, Cancellation));

        Assert.Equal("winget runs in DDT's session, which only a Write answer file step sets up, and DDTDeploy is not signed in.", exception.Message);
        Assert.Empty(_events);
        Assert.Empty(_tools.Calls);
    }

    [Fact]
    public async Task WithoutAWingetIdNothingRuns()
    {
        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(() => InstallAsync(s_sevenZip with { WingetId = " " }, Cancellation));

        Assert.Equal("It has no winget ID. Give it one in the library, then assign the sequence again.", exception.Message);
        Assert.Empty(_events);
    }

    [Fact]
    public async Task TheMembershipIsRemovedWhenWingetRunsOutOfTime()
    {
        _tools.AnswerExitCode = (_, _, _) => throw new DeploymentStepException("winget.exe was still running after 5 minutes, so it was stopped with every process it started.");

        await Assert.ThrowsAsync<DeploymentStepException>(() => InstallAsync(s_sevenZip, Cancellation));

        Assert.Equal(["join Administrators", "batch logon", "sign out batch", "leave Administrators"], _events);
        Assert.False(_account.IsAdministrator);
    }

    [Fact]
    public async Task TheMembershipIsRemovedWhenTheRunIsCancelled()
    {
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        TaskCompletionSource running = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _tools.Running = async (_, token) =>
        {
            running.SetResult();
            await Task.Delay(Timeout.Infinite, token);
        };

        Task<int> install = InstallAsync(s_sevenZip, stop.Token);
        await running.Task.WaitAsync(Cancellation);
        await stop.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => install);
        Assert.Equal(["join Administrators", "batch logon", "sign out batch", "leave Administrators"], _events);
        Assert.False(_account.IsAdministrator);
    }

    [Fact]
    public async Task AppInstallerIsRegisteredInTheAccountsOwnSessionBeforeTheAccountIsRaised()
    {
        _account.HasAlias = false;
        _tools.AnswerExitCode = (file, _, options) =>
        {
            _events.Add($"run {Path.GetFileName(file)} as {options.Account?.UserName}");
            _account.HasAlias = true;

            return 0;
        };

        await InstallAsync(s_sevenZip, Cancellation);

        Assert.Equal(
            ["session token 2", "run powershell.exe as DDTDeploy", "sign out session 2", "join Administrators", "batch logon", "run winget.exe as DDTDeploy"],
            _events[..6]);
    }

    [Fact]
    public async Task WithTheAliasThereNothingIsRegistered()
    {
        await InstallAsync(s_sevenZip, Cancellation);

        Assert.DoesNotContain(_events, step => step.StartsWith("session token", StringComparison.Ordinal));
        Assert.Single(_tools.Calls);
    }
}