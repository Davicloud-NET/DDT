// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Applications;
using DDT.Contracts.Agents;
using DDT.Contracts.Applications;
using Xunit;
using static DDT.Agent.Tests.TestApplications;

namespace DDT.Agent.Tests;

public sealed class WingetExitCodesTests
{
    private static readonly AgentApplication s_package = Tool with { Kind = ApplicationKind.Winget, Command = null, WingetId = "7zip.7zip", RestartExitCodes = [] };

    [Fact]
    public void ZeroIsInstalled() =>
        Assert.Equal(new ApplicationOutcome(ApplicationState.Installed, "Installed Tool.", ExitCode: 0), WingetExitCodes.Judge(s_package, 0));

    [Theory]
    [InlineData(unchecked((int)0x8A15002B), "0x8A15002B (APPINSTALLER_CLI_ERROR_UPDATE_NOT_APPLICABLE)")]
    [InlineData(unchecked((int)0x8A150061), "0x8A150061 (APPINSTALLER_CLI_ERROR_PACKAGE_ALREADY_INSTALLED)")]
    public void APackageWingetFindsInstalledIsThereAlready(int exitCode, string described) =>
        Assert.Equal(
            new ApplicationOutcome(ApplicationState.AlreadyThere, $"Tool is installed already, as winget says with {described}, so it was not installed again.", ExitCode: exitCode),
            WingetExitCodes.Judge(s_package, exitCode));

    [Theory]
    [InlineData(unchecked((int)0x8A150109), "0x8A150109 (APPINSTALLER_CLI_ERROR_INSTALL_REBOOT_REQUIRED_TO_FINISH)")]
    [InlineData(unchecked((int)0x8A15010B), "0x8A15010B (APPINSTALLER_CLI_ERROR_INSTALL_REBOOT_INITIATED)")]
    public void ARestartToFinishIsInstalledWithARestartDue(int exitCode, string described) =>
        Assert.Equal(
            new ApplicationOutcome(ApplicationState.Installed, $"Installed Tool, which asks for a restart with {described}.", true, exitCode),
            WingetExitCodes.Judge(s_package, exitCode));

    [Theory]
    [InlineData(unchecked((int)0x8A150014), "0x8A150014 (APPINSTALLER_CLI_ERROR_NO_APPLICATIONS_FOUND)")]
    [InlineData(unchecked((int)0x8A15010A), "0x8A15010A (APPINSTALLER_CLI_ERROR_INSTALL_REBOOT_REQUIRED_FOR_INSTALL)")]
    [InlineData(unchecked((int)0x80070005), "0x80070005")]
    [InlineData(1, "0x00000001")]
    public void AnyOtherCodeFailsWithItsHexAndWingetsName(int exitCode, string described) =>
        Assert.Equal(
            new ApplicationOutcome(ApplicationState.Failed, $"winget could not install Tool: it ended with {described}.", ExitCode: exitCode),
            WingetExitCodes.Judge(s_package, exitCode));

    [Fact]
    public void TheApplicationsOwnCodesCountForTheRest()
    {
        AgentApplication application = s_package with { SuccessExitCodes = [0, 5], RestartExitCodes = [7] };

        Assert.Equal(ApplicationState.Installed, WingetExitCodes.Judge(application, 5).State);
        Assert.True(WingetExitCodes.Judge(application, 7).RestartDue);
    }
}
