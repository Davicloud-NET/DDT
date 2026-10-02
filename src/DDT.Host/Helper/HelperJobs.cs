// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Windows;
using DDT.Host.Startup;
using DDT.Server.BootImage;

namespace DDT.Host.Helper;

// The few things the helper does for the web server: build the boot image, install the ADK, and change this computer's
// DHCP server and WDS.
public sealed class HelperJobs(HelperPaths paths)
{
    // Returns null when the job is done, or what stopped it.
    public Task<string?> RunAsync(HelperRequest request, Action<string> line, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Kind switch
        {
            HelperRequest.Build => BuildAsync(request, line, cancellationToken),
            HelperRequest.InstallAdk => Task.Run(() => InstallAdk(line), cancellationToken),
            _ when HelperNetboot.Handles(request.Kind) => new HelperNetboot(paths).RunAsync(request, line, cancellationToken),
            _ => Task.FromResult<string?>($"The helper does nothing called {request.Kind}."),
        };
    }

    private async Task<string?> BuildAsync(HelperRequest request, Action<string> line, CancellationToken cancellationToken)
    {
        string? problem = await new HelperBootImageBuild(paths).RunAsync(request, line, cancellationToken).ConfigureAwait(false);

        // Where WDS offers DDT in its boot menu, it gets the new build too. A failure there leaves the build as it is.
        if (problem is null && WindowsServices.State("WDSServer").Installed)
        {
            HelperRequest refresh = new() { Kind = HelperRequest.WdsRefresh };

            if (await new HelperNetboot(paths).RunAsync(refresh, line, cancellationToken).ConfigureAwait(false) is { } stale)
            {
                line($"Windows Deployment Services still offers the build before this one: {stale}");
            }
        }

        return problem;
    }

    private static string? InstallAdk(Action<string> line)
    {
        if (!OperatingSystem.IsWindows())
        {
            return "The Windows ADK installs on Windows only.";
        }

        using LineWriter output = new(line);

        return AdkSetup.ForThisServer().Install(output);
    }
}
