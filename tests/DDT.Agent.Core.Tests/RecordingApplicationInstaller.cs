// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Applications;
using DDT.Contracts.Applications;

namespace DDT.Agent.Tests;

// Installs Command applications by recording them in Events as "install <name>". ExitCode answers each, or throws;
// without it every one ends with 0. Seen runs inside each install, while its files are there.
internal sealed class RecordingApplicationInstaller(List<string> events) : IApplicationKindInstaller
{
    public ApplicationKind Kind => ApplicationKind.Command;

    public List<ApplicationLaunch> Launches { get; } = [];

    public Func<ApplicationLaunch, int>? ExitCode { get; set; }

    public Action<ApplicationLaunch>? Seen { get; set; }

    public Task<int> InstallAsync(ApplicationLaunch launch, CancellationToken cancellationToken)
    {
        events.Add($"install {launch.Application.Name}");
        Launches.Add(launch);

        try
        {
            Seen?.Invoke(launch);

            return Task.FromResult(ExitCode?.Invoke(launch) ?? 0);
        }
        catch (Exception exception)
        {
            return Task.FromException<int>(exception);
        }
    }
}
