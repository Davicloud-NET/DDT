// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Applications;
using DDT.Core.Applications.Msix;

namespace DDT.Agent.Applications;

// Provisions an MSIX application for every user with DISM, which adds it to each account at its next sign-in. The
// layout is read as the server read it when the application was saved. Windows can drop a package DISM provisioned
// shortly after setup, so it counts only once Windows lists it. Without packages, as in a dry run, DISM's word counts.
public sealed class MsixApplicationInstaller(IToolRunner tools, IProvisionedPackages? packages, AgentLog log, TimeProvider timeProvider)
    : IApplicationKindInstaller
{
    // Windows dropped the package about 2 s after DISM ended.
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan RetryWait = TimeSpan.FromSeconds(10);
    public const int Retries = 8;

    public ApplicationKind Kind => ApplicationKind.Msix;

    public ApplicationOutcome Judge(AgentApplication application, int exitCode) => ApplicationInstaller.ByExitCodes(application, exitCode);

    public static IReadOnlyList<string> Arguments(string folder, MsixLayout layout, string logFile)
    {
        ArgumentNullException.ThrowIfNull(layout);

        return
        [
            "/Online",
            "/Add-ProvisionedAppxPackage",
            $"/PackagePath:{MsixFolderLayout.FullPath(folder, layout.Main.Path)}",
            .. layout.Dependencies.Select(dependency => $"/DependencyPackagePath:{MsixFolderLayout.FullPath(folder, dependency.Path)}"),
            layout.License is { } license ? $"/LicensePath:{MsixFolderLayout.FullPath(folder, license)}" : "/SkipLicense",
            $"/LogPath:{logFile}",
        ];
    }

    // The application's own rule, or else the family and version of the package it provisions.
    public static PackageFamilyDetection KeptRule(AgentApplication application, MsixLayout layout)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(layout);

        return application.Detection as PackageFamilyDetection
            ?? new PackageFamilyDetection(layout.Main.Manifest.FamilyName, layout.Main.Manifest.Version);
    }

    public async Task<int> InstallAsync(ApplicationLaunch launch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launch);

        AgentApplication application = launch.Application;
        MsixLayoutResult read = MsixFolderLayout.Read(launch.WorkingDirectory);

        if (read.Layout is not { } layout)
        {
            throw new DeploymentStepException(read.Problem?.Text ?? "Its files hold no package DISM can provision.");
        }

        log.Information(
            $"Provisioning {layout.Main.Path} with {layout.Dependencies.Count} framework packages and {(layout.License is null ? "no licence" : layout.License)}.");
        TimeSpan timeout = TimeSpan.FromMinutes(application.TimeoutMinutes);
        long started = timeProvider.GetTimestamp();

        for (int tries = 1; ; tries++)
        {
            int exitCode = await ProvisionAsync(launch, layout, timeout - timeProvider.GetElapsedTime(started), cancellationToken).ConfigureAwait(false);

            if (packages is null || !(application.SuccessExitCodes.Contains(exitCode) || application.RestartExitCodes.Contains(exitCode)))
            {
                return exitCode;
            }

            await Task.Delay(Settle, timeProvider, cancellationToken).ConfigureAwait(false);

            if (IsKept(KeptRule(application, layout)))
            {
                return exitCode;
            }

            if (tries > Retries || timeProvider.GetElapsedTime(started) + RetryWait >= timeout)
            {
                throw new DeploymentStepException($"Windows did not keep {layout.Main.Path} after {tries} tries.");
            }

            log.Warning($"Windows did not keep {layout.Main.Path}, so it is provisioned again in {(int)RetryWait.TotalSeconds} s.");
            await Task.Delay(RetryWait, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<int> ProvisionAsync(ApplicationLaunch launch, MsixLayout layout, TimeSpan timeout, CancellationToken cancellationToken)
    {
        string logFile = Path.Combine(launch.Logs, $"dism-{launch.Application.Id:D}.log");
        ToolRunOptions options = new(launch.WorkingDirectory, launch.Environment, timeout);

        return await tools.RunForExitCodeAsync(InjectDriversStepRunner.DismPath, Arguments(launch.WorkingDirectory, layout, logFile), options, cancellationToken)
            .ConfigureAwait(false);
    }

    private bool IsKept(PackageFamilyDetection rule) =>
        packages?.FullNames().Any(fullName => WindowsApplicationDetector.IsProvisioned(fullName, rule)) ?? true;
}
