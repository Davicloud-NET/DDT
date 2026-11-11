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
// layout is read as the server read it when the application was saved.
public sealed class MsixApplicationInstaller(IToolRunner tools, AgentLog log) : IApplicationKindInstaller
{
    public ApplicationKind Kind => ApplicationKind.Msix;

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

    public async Task<int> InstallAsync(ApplicationLaunch launch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launch);

        AgentApplication application = launch.Application;
        MsixLayoutResult read = MsixFolderLayout.Read(launch.WorkingDirectory);

        if (read.Layout is not { } layout)
        {
            throw new DeploymentStepException(read.Problem?.Text ?? "Its files hold no package DISM can provision.");
        }

        string logFile = Path.Combine(launch.Logs, $"dism-{application.Id:D}.log");
        log.Information(
            $"Provisioning {layout.Main.Path} with {layout.Dependencies.Count} framework packages and {(layout.License is null ? "no licence" : layout.License)}.");
        ToolRunOptions options = new(launch.WorkingDirectory, launch.Environment, TimeSpan.FromMinutes(application.TimeoutMinutes));

        return await tools.RunForExitCodeAsync(InjectDriversStepRunner.DismPath, Arguments(launch.WorkingDirectory, layout, logFile), options, cancellationToken)
            .ConfigureAwait(false);
    }
}
