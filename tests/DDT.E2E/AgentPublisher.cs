// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;

namespace DDT.E2E;

// Publishes the agent with build\Publish-Agent.ps1, so the tests never run an agent older than its sources.
// Without the Visual C++ build tools, which NativeAOT links with, the tests are skipped. Any other failure fails them.
// The agent for Linux is published in a container, which the tests do not start: they are handed the file.
internal static class AgentPublisher
{
    // Names a published agent for the tests to run instead of publishing one: ddt-agent of Publish-LinuxAgent.ps1 on
    // Linux, ddt-agent.exe on Windows.
    public const string HandedVariable = "DDT_E2E_AGENT";

    public static string FileName => OperatingSystem.IsWindows() ? "ddt-agent.exe" : "ddt-agent";

    // The component NativeAOT asks vswhere for to find its linker.
    private const string VisualCppTools = "Microsoft.VisualStudio.Component.VC.Tools.x86.x64";

    private static readonly TimeSpan s_timeout = TimeSpan.FromMinutes(10);

    // Returns why the tests are skipped, or null once the agent is in output.
    public static async Task<string?> PublishAsync(string output, string logPath, CancellationToken cancellationToken)
    {
        if (Environment.GetEnvironmentVariable(HandedVariable) is { Length: > 0 } handed)
        {
            Take(handed, output);

            return null;
        }

        if (!OperatingSystem.IsWindows())
        {
            return $"The agent for this system is published in a container. Name the file build/Publish-LinuxAgent.ps1 wrote in {HandedVariable}.";
        }

        if (!await VisualCppToolsInstalledAsync(cancellationToken).ConfigureAwait(false))
        {
            return $"build\\Publish-Agent.ps1 publishes the agent with the Visual C++ build tools ({VisualCppTools}), which vswhere does not find.";
        }

        await RunScriptAsync(output, logPath, cancellationToken).ConfigureAwait(false);

        return null;
    }

    private static async Task RunScriptAsync(string output, string logPath, CancellationToken cancellationToken)
    {
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

        // The job kills everything the publish starts. So the publish must not start an MSBuild node or a compiler
        // server that a build outside the tests could share at that moment.
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
            // Also when the tests were cancelled. Nothing may keep writing into the directory the tests delete.
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
    }

    // A copy, so nothing the agent writes beside itself lands where the file came from. A file handed on by a
    // build may have lost the mode that makes it a program.
    private static void Take(string handed, string output)
    {
        if (!File.Exists(handed))
        {
            throw new FileNotFoundException($"{HandedVariable} names {handed}, which is not there.");
        }

        Directory.CreateDirectory(output);
        string agent = Path.Combine(output, FileName);
        File.Copy(handed, agent, overwrite: true);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(agent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
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
