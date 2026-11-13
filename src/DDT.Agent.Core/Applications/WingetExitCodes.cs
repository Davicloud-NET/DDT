// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Agents;

namespace DDT.Agent.Applications;

// What winget's exit code means for an application. winget's own codes come first, then the application's success and
// restart codes.
public static class WingetExitCodes
{
    public const int UpdateNotApplicable = unchecked((int)0x8A15002B);
    public const int PackageAlreadyInstalled = unchecked((int)0x8A150061);
    public const int RebootRequiredToFinish = unchecked((int)0x8A150109);
    public const int RebootInitiated = unchecked((int)0x8A15010B);

    public static ApplicationOutcome Judge(AgentApplication application, int exitCode)
    {
        ArgumentNullException.ThrowIfNull(application);

        return exitCode switch
        {
            0 => new ApplicationOutcome(ApplicationState.Installed, $"Installed {application.Name}.", ExitCode: exitCode),
            UpdateNotApplicable or PackageAlreadyInstalled => new ApplicationOutcome(
                ApplicationState.AlreadyThere,
                $"{application.Name} is installed already, as winget says with {Describe(exitCode)}, so it was not installed again.",
                ExitCode: exitCode),
            RebootRequiredToFinish or RebootInitiated => new ApplicationOutcome(
                ApplicationState.Installed,
                $"Installed {application.Name}, which asks for a restart with {Describe(exitCode)}.",
                true,
                exitCode),
            _ when application.SuccessExitCodes.Contains(exitCode) || application.RestartExitCodes.Contains(exitCode) =>
                ApplicationInstaller.ByExitCodes(application, exitCode),
            _ => new ApplicationOutcome(ApplicationState.Failed, $"winget could not install {application.Name}: it ended with {Describe(exitCode)}.", ExitCode: exitCode),
        };
    }

    // 0x8A150014 (APPINSTALLER_CLI_ERROR_NO_APPLICATIONS_FOUND), or the code alone when winget does not name it.
    public static string Describe(int exitCode)
    {
        string hex = string.Create(CultureInfo.InvariantCulture, $"0x{exitCode:X8}");

        return WingetReturnCodes.NameOf(exitCode) is { } name ? $"{hex} ({name})" : hex;
    }
}
