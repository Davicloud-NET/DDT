// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.Globalization;
using DDT.Server.BootImage;

namespace DDT.Host.Helper;

// Builds the boot image as SYSTEM with the Build-BootImage.ps1 of the release, in the helper's own folder, and
// publishes it into the boot directory. The agent, the console and the script come from the program folder, which
// only administrators can change. Tests pass a process of their own in place of PowerShell.
public sealed class HelperBootImageBuild(HelperPaths paths, Func<ProcessStartInfo, Action<string>, CancellationToken, Task<int>> run)
{
    public HelperBootImageBuild(HelperPaths paths)
        : this(paths, ScriptProcess.RunAsync)
    {
    }

    // Returns null when the build is published and served, or what stopped it.
    public async Task<string?> RunAsync(HelperRequest request, Action<string> line, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(line);

        if (HelperRequestRules.BuildProblem(request, paths) is { } invalid)
        {
            return invalid;
        }

        if (!File.Exists(paths.Script))
        {
            return $"{paths.Script} is missing. It comes with DDT's installer.";
        }

        string name = request.Name ?? string.Empty;
        string work = Path.Combine(paths.WorkRoot, name);

        try
        {
            ProtectedFolder.Create(paths.WorkRoot);
            Directory.CreateDirectory(work);

            string? drivers = null;

            if (request.Drivers.Count > 0)
            {
                drivers = Path.Combine(work, "drivers");

                if (await DriverPackages.UnpackAsync(paths, request, drivers, cancellationToken).ConfigureAwait(false) is { } missing)
                {
                    return missing;
                }
            }

            string built = Path.Combine(work, "out");
            int exit = await run(Script(request, work, built, drivers), line, cancellationToken).ConfigureAwait(false);

            if (exit != 0)
            {
                return $"Build-BootImage.ps1 ended with exit code {exit}. Its last lines say why.";
            }

            BuildPublisher.Publish(built, paths.BootDirectory, name);
            line($"The server serves this build, {name}, from now on.");

            return null;
        }
        finally
        {
            if (Directory.Exists(work))
            {
                Directory.Delete(work, recursive: true);
            }
        }
    }

    // Every value was checked by HelperRequestRules, and every path is the helper's own.
    private ProcessStartInfo Script(HelperRequest request, string work, string built, string? drivers)
    {
        ProcessStartInfo start = new(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"));

        foreach (string argument in new[]
        {
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", paths.Script,
            "-Destination", built,
            "-WorkDirectory", Path.Combine(work, "winpe"),
            "-ServerUrl", HelperRequestRules.ServerUrl(request) ?? string.Empty,
        })
        {
            start.ArgumentList.Add(argument);
        }

        if (request.KeyboardLayout is not null)
        {
            start.ArgumentList.Add("-KeyboardLayout");
            start.ArgumentList.Add(request.KeyboardLayout);
        }

        if (request.TftpWindowSize is { } window)
        {
            start.ArgumentList.Add("-TftpWindowSize");
            start.ArgumentList.Add(window.ToString(CultureInfo.InvariantCulture));
        }

        if (request.SkipPowerShell)
        {
            start.ArgumentList.Add("-SkipPowerShell");
        }

        if (drivers is not null)
        {
            start.ArgumentList.Add("-ServerDriverPath");
            start.ArgumentList.Add(drivers);
        }

        return start;
    }
}
