// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent.Applications;
using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

// The account's profile is a temporary folder. PowerShell is played by the tool runner, which writes its error file.
public sealed class AppInstallerRegistrationTests : IDisposable
{
    private readonly List<string> _events = [];
    private readonly RecordingToolRunner _tools = new();
    private readonly ImmediateTimeProvider _time = new();
    private readonly StringWriter _output = new();
    private readonly string _profile = Directory.CreateTempSubdirectory("ddt-profile-").FullName;
    private readonly FakeDeployAccount _account;

    public AppInstallerRegistrationTests()
    {
        _account = new FakeDeployAccount(_events) { ProfilePath = _profile, HasAlias = false };
        _tools.AnswerExitCode = (_, _, _) =>
        {
            _account.HasAlias = true;

            return 0;
        };
    }

    private string ErrorFile => AppInstallerRegistration.ErrorFileIn(_profile);

    public void Dispose()
    {
        _output.Dispose();
        Directory.Delete(_profile, recursive: true);
    }

    private Task EnsureAsync() =>
        new AppInstallerRegistration(_tools, _account, new AgentLog(TimeProvider.System, _output), _time).EnsureAsync(2, _profile, TestContext.Current.CancellationToken);

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
                    + $"catch {{ [IO.File]::WriteAllText('{ErrorFile}', ($_ | Out-String)); exit 1 }}; exit 0")],
            _tools.Calls);
        ToolRunOptions options = Assert.Single(_tools.Options);
        Assert.Equal((_profile, AppInstallerRegistration.Timeout, "DDTDeploy"), (options.WorkingDirectory, options.Timeout, options.Account?.UserName));
        Assert.Equal(["session token 2", "sign out session 2"], _events);
        Assert.True(Directory.Exists(Path.GetDirectoryName(ErrorFile)));
    }

    [Fact]
    public void AQuoteInTheFilesPathIsDoubledForPowerShell() =>
        Assert.Contains("'C:\\Users\\O''Brien\\x.txt'", AppInstallerRegistration.Arguments(@"C:\Users\O'Brien\x.txt")[^1], StringComparison.Ordinal);

    [Fact]
    public async Task WithTheAliasThereNothingRuns()
    {
        _account.HasAlias = true;

        await EnsureAsync();

        Assert.Empty(_tools.Calls);
        Assert.Empty(_events);
    }

    [Fact]
    public async Task PowerShellsErrorFromItsFileGoesToTheLogAndTheFileIsDeleted()
    {
        _tools.AnswerExitCode = (_, _, _) =>
        {
            File.WriteAllText(ErrorFile, "Add-AppxPackage : Fehler bei Bereitstellung. HRESULT: 0x80070005, Zugriff verweigert\r\n\r\n    + CategoryInfo : Sicherheitsfehler\r\n", Encoding.UTF8);

            return 1;
        };

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(EnsureAsync);

        Assert.Equal("App Installer could not be registered for DDTDeploy. PowerShell's error is in the machine log.", exception.Message);
        string log = _output.ToString();
        Assert.Contains("Add-AppxPackage : Fehler bei Bereitstellung. HRESULT: 0x80070005, Zugriff verweigert", log, StringComparison.Ordinal);
        Assert.Contains("+ CategoryInfo : Sicherheitsfehler", log, StringComparison.Ordinal);
        Assert.False(File.Exists(ErrorFile));
    }

    [Fact]
    public async Task AnErrorFileLeftFromBeforeIsDeletedFirst()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ErrorFile)!);
        File.WriteAllText(ErrorFile, "an old error");

        await EnsureAsync();

        Assert.DoesNotContain("an old error", _output.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(ErrorFile));
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
