// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Applications;
using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class AppInstallerRegistrationTests
{
    private const string Profile = @"C:\Users\DDTDeploy";

    private readonly List<string> _events = [];
    private readonly RecordingToolRunner _tools = new();
    private readonly ImmediateTimeProvider _time = new();
    private readonly FakeDeployAccount _account;

    public AppInstallerRegistrationTests()
    {
        _account = new FakeDeployAccount(_events) { ProfilePath = Profile, HasAlias = false };
        _tools.AnswerExitCode = (_, _, _) =>
        {
            _account.HasAlias = true;

            return 0;
        };
    }

    private Task EnsureAsync() =>
        new AppInstallerRegistration(_tools, _account, new AgentLog(TimeProvider.System, TextWriter.Null), _time).EnsureAsync(2, Profile, TestContext.Current.CancellationToken);

    [Fact]
    public async Task PowerShellRegistersAppInstallerAsTheAccountInItsSessionInItsProfile()
    {
        await EnsureAsync();

        Assert.Equal(
            [RecordingToolRunner.CommandLine(
                AppInstallerRegistration.PowerShellPath,
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy",
                "Bypass",
                "-WindowStyle",
                "Hidden",
                "-Command",
                "try { Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe -ErrorAction Stop } "
                    + "catch { [Console]::Error.WriteLine(($_ | Out-String).TrimEnd()); exit 1 }; exit 0")],
            _tools.Calls);
        ToolRunOptions options = Assert.Single(_tools.Options);
        Assert.Equal((Profile, AppInstallerRegistration.Timeout, "DDTDeploy"), (options.WorkingDirectory, options.Timeout, options.Account?.UserName));
        Assert.Equal(["session token 2", "sign out session 2"], _events);
    }

    [Fact]
    public async Task WithTheAliasThereNothingRuns()
    {
        _account.HasAlias = true;

        await EnsureAsync();

        Assert.Empty(_tools.Calls);
        Assert.Empty(_events);
    }

    [Fact]
    public async Task APowerShellThatFailsFailsTheRegistration()
    {
        _tools.AnswerExitCode = (_, _, _) => 1;

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(EnsureAsync);

        Assert.Equal("App Installer could not be registered for DDTDeploy. PowerShell's error is in the machine log.", exception.Message);
        Assert.Empty(_time.Delays);
    }

    [Fact]
    public async Task ARegistrationThatLeavesNoAliasFails()
    {
        _tools.AnswerExitCode = (_, _, _) => 0;

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(EnsureAsync);

        Assert.Equal("App Installer was registered for DDTDeploy, but winget is still missing from its profile.", exception.Message);
        Assert.Equal(AppInstallerRegistration.AliasChecks - 1, _time.Delays.Count);
    }
}
