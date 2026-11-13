// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Applications;

namespace DDT.Agent.Applications;

// Installs one application: its detection rule first, then its files and its kind's installer, which starts only once
// markStarted has saved that it did. What goes wrong with the application is its outcome; a stop is thrown.
public sealed class ApplicationInstaller(
    IApplicationDetector detector,
    IReadOnlyList<IApplicationKindInstaller> installers,
    ApplicationFolders folders,
    AgentLog log)
{
    // An installer cut off this often is not started again, so one that restarts the machine itself cannot loop.
    public const int MaxAttempts = 2;

    public static string NotYet(AgentApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return application.Kind switch
        {
            ApplicationKind.Winget => $"{application.Name} is a winget package, which only the agent in the installed Windows installs.",
            _ => $"{application.Name} is of a kind this agent cannot install.",
        };
    }

    public static string ExitCodeFailed(AgentApplication application, int exitCode)
    {
        ArgumentNullException.ThrowIfNull(application);

        return application.SuccessExitCodes.Count == 0
            ? $"{application.Name} ended with exit code {exitCode}, which is not one of its restart codes ({string.Join(", ", application.RestartExitCodes)})."
            : $"{application.Name} ended with exit code {exitCode}, which is not one of its success codes ({string.Join(", ", application.SuccessExitCodes)}).";
    }

    public async Task<ApplicationOutcome> InstallAsync(
        ApplicationInstall install,
        Func<Task> markStarted,
        IProgress<int> percent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(install);
        ArgumentNullException.ThrowIfNull(markStarted);

        AgentApplication application = install.Application;
        bool again = install.Attempt > 1;

        if (application.Detection is { } rule && detector.IsInstalled(rule))
        {
            string found = ApplicationDetections.Describe(rule);

            return again
                ? new ApplicationOutcome(ApplicationState.Installed, $"{application.Name} was installed before the machine restarted, as {found} shows.")
                : new ApplicationOutcome(ApplicationState.AlreadyThere, $"{application.Name} is installed already, as {found} shows, so it was not installed again.");
        }

        if (install.Attempt > MaxAttempts)
        {
            return Failed($"The machine restarted twice while {application.Name} installed, so it is not installed a third time.");
        }

        if (installers.FirstOrDefault(installer => installer.Kind == application.Kind) is not { } kind)
        {
            return Failed(NotYet(application));
        }

        return await RunAsync(kind, install, markStarted, percent, cancellationToken).ConfigureAwait(false);
    }

    // The application's success and restart codes judge the exit code.
    public static ApplicationOutcome ByExitCodes(AgentApplication application, int exitCode)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (application.SuccessExitCodes.Contains(exitCode))
        {
            return new ApplicationOutcome(ApplicationState.Installed, $"Installed {application.Name}.", ExitCode: exitCode);
        }

        return application.RestartExitCodes.Contains(exitCode)
            ? new ApplicationOutcome(ApplicationState.Installed, $"Installed {application.Name}, which asks for a restart with exit code {exitCode}.", true, exitCode)
            : Failed(ExitCodeFailed(application, exitCode), exitCode);
    }

    private static ApplicationOutcome Failed(string message, int? exitCode = null) => new(ApplicationState.Failed, message, ExitCode: exitCode);

    // The unpacked files are deleted however the installer ended, also when unpacking failed halfway.
    private async Task<ApplicationOutcome> RunAsync(
        IApplicationKindInstaller kind,
        ApplicationInstall install,
        Func<Task> markStarted,
        IProgress<int> percent,
        CancellationToken cancellationToken)
    {
        AgentApplication application = install.Application;
        string? package = folders.PackageOf(install);

        try
        {
            await folders.UnpackAsync(install, percent, cancellationToken).ConfigureAwait(false);
            string scripts = folders.Scripts();
            ApplicationLaunch launch = new(application, package ?? scripts, scripts, folders.Logs(), ApplicationEnvironment.Of(install, package));

            await markStarted().ConfigureAwait(false);
            log.Information(install.Attempt > 1
                ? $"Installing {application.Name} again, because the machine restarted while it installed."
                : $"Installing {application.Name}.");

            return kind.Judge(application, await kind.InstallAsync(launch, cancellationToken).ConfigureAwait(false));
        }
        catch (DeploymentStepException exception)
        {
            return Failed($"{application.Name} could not be installed: {exception.Message}");
        }
        finally
        {
            if (package is not null)
            {
                Leftovers.Delete(package, log);
            }
        }
    }
}
