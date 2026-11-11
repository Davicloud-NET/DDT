// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Applications;

namespace DDT.Agent.Applications;

// Installs a winget package for the machine. winget does not start as SYSTEM, so it runs as DDT's session account,
// raised to an administrator for each package. Its alias only exists once App Installer is registered for the account,
// which works only from inside the account's session.
public sealed class WingetApplicationInstaller(
    IToolRunner tools,
    IDeployAccount account,
    AgentLog log,
    TimeProvider timeProvider) : IApplicationKindInstaller
{
    public const string AppInstallerFamily = "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe";

    public static readonly TimeSpan RegistrationTimeout = TimeSpan.FromMinutes(5);

    // The alias can show up a moment after the registration ended.
    public static readonly TimeSpan AliasWait = TimeSpan.FromSeconds(1);
    public const int AliasChecks = 10;

    public ApplicationKind Kind => ApplicationKind.Winget;

    public static string PowerShellPath => Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    public static string NoSession(string userName) =>
        $"winget runs in DDT's session, which only a Write answer file step sets up, and {userName} is not signed in.";

    public static string AliasIn(string profile) => Path.Combine(profile, "AppData", "Local", "Microsoft", "WindowsApps", "winget.exe");

    public static IReadOnlyList<string> Arguments(string id, string? version) =>
    [
        "install",
        "--id",
        id,
        "--exact",
        .. string.IsNullOrWhiteSpace(version) ? Array.Empty<string>() : ["--version", version.Trim()],
        "--source",
        "winget",
        "--scope",
        "machine",
        "--silent",
        "--accept-source-agreements",
        "--accept-package-agreements",
        "--disable-interactivity",
    ];

    public static IReadOnlyList<string> RegistrationArguments() =>
    [
        "-NoProfile",
        "-NonInteractive",
        "-ExecutionPolicy",
        "Bypass",
        "-WindowStyle",
        "Hidden",
        "-Command",
        $"Add-AppxPackage -RegisterByFamilyName -MainPackage {AppInstallerFamily}; exit [int](-not $?)",
    ];

    public ApplicationOutcome Judge(AgentApplication application, int exitCode) => WingetExitCodes.Judge(application, exitCode);

    public async Task<int> InstallAsync(ApplicationLaunch launch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launch);

        AgentApplication application = launch.Application;

        if (string.IsNullOrWhiteSpace(application.WingetId))
        {
            throw new DeploymentStepException("It has no winget ID. Give it one in the library, then assign the sequence again.");
        }

        int session = account.SessionId() ?? throw new DeploymentStepException(NoSession(account.UserName));
        string profile = account.Profile();
        await RegisterAsync(session, profile, cancellationToken).ConfigureAwait(false);

        using DeployAccountElevation raised = await DeployAccountElevation.RaiseAsync(account, log, cancellationToken).ConfigureAwait(false);
        ToolRunOptions options = new(profile, launch.Environment, TimeSpan.FromMinutes(application.TimeoutMinutes), raised.Session, WingetOutput.Line);
        IReadOnlyList<string> arguments = Arguments(application.WingetId.Trim(), application.WingetVersion);

        return await tools.RunForExitCodeAsync(AliasIn(profile), arguments, options, cancellationToken).ConfigureAwait(false);
    }

    // As the account itself, without administrator's rights: a registration from session 0 is refused.
    private async Task RegisterAsync(int session, string profile, CancellationToken cancellationToken)
    {
        if (account.HasWinget())
        {
            return;
        }

        log.Information($"winget is not set up for {account.UserName} yet, so App Installer is registered in its session.");
        int exitCode;

        using (IAccountSession user = await account.InSessionAsync(session, cancellationToken).ConfigureAwait(false))
        {
            ToolRunOptions options = new(profile, null, RegistrationTimeout, user);
            exitCode = await tools.RunForExitCodeAsync(PowerShellPath, RegistrationArguments(), options, cancellationToken).ConfigureAwait(false);
        }

        if (exitCode != 0)
        {
            throw new DeploymentStepException($"App Installer could not be registered for {account.UserName}. PowerShell's output is in the machine log.");
        }

        for (int check = 1; !account.HasWinget(); check++)
        {
            if (check == AliasChecks)
            {
                throw new DeploymentStepException($"App Installer was registered for {account.UserName}, but winget is still missing from its profile.");
            }

            await Task.Delay(AliasWait, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }
}
