// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Applications;

namespace DDT.Agent.Applications;

// Installs a winget package for the machine. winget does not start as SYSTEM, so it runs as DDT's session account,
// raised to an administrator for each package. Its alias only exists once App Installer is registered for the account.
public sealed class WingetApplicationInstaller(
    IToolRunner tools,
    IDeployAccount account,
    AgentLog log,
    TimeProvider timeProvider) : IApplicationKindInstaller
{
    private readonly AppInstallerRegistration _registration = new(tools, account, log, timeProvider);

    public ApplicationKind Kind => ApplicationKind.Winget;

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
        await _registration.EnsureAsync(session, profile, cancellationToken).ConfigureAwait(false);

        using DeployAccountElevation raised = await DeployAccountElevation.RaiseAsync(account, log, cancellationToken).ConfigureAwait(false);
        ToolRunOptions options = new(profile, launch.Environment, TimeSpan.FromMinutes(application.TimeoutMinutes), raised.Session, WingetOutput.Line);
        IReadOnlyList<string> arguments = Arguments(application.WingetId.Trim(), application.WingetVersion);

        return await tools.RunForExitCodeAsync(AliasIn(profile), arguments, options, cancellationToken).ConfigureAwait(false);
    }
}
