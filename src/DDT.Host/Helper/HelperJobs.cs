// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Host.Startup;
using DDT.Server.BootImage;

namespace DDT.Host.Helper;

// The few things the helper does for the web server: build the boot image, and install the ADK.
public sealed class HelperJobs(HelperPaths paths)
{
    // Returns null when the job is done, or what stopped it.
    public Task<string?> RunAsync(HelperRequest request, Action<string> line, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Kind switch
        {
            HelperRequest.Build => new HelperBootImageBuild(paths).RunAsync(request, line, cancellationToken),
            HelperRequest.InstallAdk => Task.Run(() => InstallAdk(line), cancellationToken),
            _ => Task.FromResult<string?>($"The helper does nothing called {request.Kind}."),
        };
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
