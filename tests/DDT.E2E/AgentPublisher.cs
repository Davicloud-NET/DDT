// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;

namespace DDT.E2E;

// Publishes the agent with build\Publish-Agent.ps1, so the tests never run an agent older than its sources.
// Without the Visual C++ build tools, which NativeAOT links with, the tests are skipped; any other failure fails them.
internal static class AgentPublisher
{
    public const string FileName = "ddt-agent.exe";

    // What NativeAOT asks vswhere for to find its linker.
    private const string VisualCppTools = "Microsoft.VisualStudio.Component.VC.Tools.x86.x64";

    private static readonly TimeSpan s_timeout = TimeSpan.FromMinutes(10);

    // Why the tests are skipped, or null once the agent is in output.
    public static async Task<string?> PublishAsync(string output, string logPath, CancellationToken cancellationToken)
    {
        if (!await VisualCppToolsInstalledAsync(cancellationToken).ConfigureAwait(false))
        {
            return $"build\\Publish-Agent.ps1 publishes the agent with the Visual C++ build tools ({VisualCppTools}), which vswhere does not find.";
        }

        ProcessStartInfo start = new(
            "powershell.exe",
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", RepositoryPaths.PublishAgentScript, "-Output", output])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // The job ends everything the publish starts, so it must start no MSBuild node or compiler server that a
        // build outside the tests could be sharing at that moment.
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["UseSharedCompilation"] = "false";

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("powershell.exe did not start.");
        KillOnExitJob.Add(process);
        using OutputLines lines = new(process, logPath);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.StandardInput.Close();

        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(s_timeout);

        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"build\\Publish-Agent.ps1 did not finish within {s_timeout.TotalMinutes:0} minutes:{Environment.NewLine}{lines.Tail()}");
        }
        finally
        {
            // Also when the tests were cancelled: nothing may go on writing into the directory the tests delete.
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.WaitForExit();
        }

        if (process.ExitCode != 0 || !File.Exists(Path.Combine(output, FileName)))
        {
            throw new InvalidOperationException(
                $"build\\Publish-Agent.ps1 could not publish the agent (exit code {process.ExitCode}):{Environment.NewLine}{lines.Tail(15)}");
        }

        return null;
    }

    private static async Task<bool> VisualCppToolsInstalledAsync(CancellationToken cancellationToken)
    {
        string vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio",
            "Installer",
            "vswhere.exe");

        if (!File.Exists(vswhere))
        {
            return false;
        }

        ProcessStartInfo start = new(vswhere, ["-latest", "-prerelease", "-products", "*", "-requires", VisualCppTools, "-property", "installationPath"])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{vswhere} did not start.");
        string installation = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(installation);
    }
}
