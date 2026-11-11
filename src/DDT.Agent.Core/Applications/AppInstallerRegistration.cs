// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;

namespace DDT.Agent.Applications;

// Registers App Installer for DDT's session account, which puts winget's alias into its profile. Windows refuses that
// from session 0, so PowerShell runs as the account in its own session. A start there inherits no pipes, so PowerShell
// writes what went wrong to a file in the account's temp folder.
public sealed class AppInstallerRegistration(IToolRunner tools, IDeployAccount account, AgentLog log, TimeProvider timeProvider)
{
    public const string Family = "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe";

    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    // The alias can show up a moment after the registration ended.
    public static readonly TimeSpan AliasWait = TimeSpan.FromSeconds(1);
    public const int AliasChecks = 10;

    public static string PowerShellPath => Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    public static string ErrorFileIn(string profile) => Path.Combine(profile, "AppData", "Local", "Temp", "ddt-app-installer.txt");

    // In Windows PowerShell 5.1 $? is not reliable after a failed cmdlet, a terminating error is.
    public static IReadOnlyList<string> Arguments(string errorFile)
    {
        ArgumentNullException.ThrowIfNull(errorFile);

        string quoted = errorFile.Replace("'", "''", StringComparison.Ordinal);

        return
        [
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-WindowStyle",
            "Hidden",
            "-Command",
            $"try {{ Add-AppxPackage -RegisterByFamilyName -MainPackage {Family} -ErrorAction Stop }} "
                + $"catch {{ [IO.File]::WriteAllText('{quoted}', ($_ | Out-String)); exit 1 }}; exit 0",
        ];
    }

    public async Task EnsureAsync(int session, string profile, CancellationToken cancellationToken)
    {
        if (account.HasWinget())
        {
            return;
        }

        log.Information($"winget is not set up for {account.UserName} yet, so App Installer is registered in its session.");
        string errorFile = ErrorFileIn(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(errorFile) ?? profile);
        Leftovers.Delete(errorFile, log);
        int exitCode;

        using (IAccountSession user = await account.InSessionAsync(session, cancellationToken).ConfigureAwait(false))
        {
            ToolRunOptions options = new(profile, null, Timeout, user);
            exitCode = await tools.RunForExitCodeAsync(PowerShellPath, Arguments(errorFile), options, cancellationToken).ConfigureAwait(false);
        }

        LogErrors(errorFile);

        if (exitCode != 0)
        {
            throw new DeploymentStepException($"App Installer could not be registered for {account.UserName}. PowerShell's error is in the machine log.");
        }

        await WaitForAliasAsync(cancellationToken).ConfigureAwait(false);
    }

    private void LogErrors(string errorFile)
    {
        if (!File.Exists(errorFile))
        {
            return;
        }

        try
        {
            foreach (string line in File.ReadAllLines(errorFile, Encoding.UTF8).Where(line => !string.IsNullOrWhiteSpace(line)))
            {
                log.Warning(line.TrimEnd());
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"PowerShell's error could not be read from {errorFile} ({exception.Message}).");
        }

        Leftovers.Delete(errorFile, log);
    }

    private async Task WaitForAliasAsync(CancellationToken cancellationToken)
    {
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
