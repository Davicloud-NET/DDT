// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Runs a sequence author's script with cmd or Windows PowerShell, in either phase. The script file goes where the run
// keeps its files: under workDirectory, the agent's own directory, before the disk is partitioned, and in the run's
// directory on the Windows volume after. A package is unpacked there and becomes the working directory. The exit code
// decides: a restart code restarts the machine before the next step, a success code is done, any other fails.
public sealed class RunScriptStepRunner(IToolRunner tools, RunDownloads downloads, RunSession session, AgentLog log, string workDirectory)
{
    public const string NoPowerShellMessage =
        "This boot image has no Windows PowerShell, which a script of this sequence needs in Windows PE. Build the boot image " +
        "again with build\\Build-BootImage.ps1 without -SkipPowerShell.";

    public static string CmdPath => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    public static string PowerShellPath => Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    // cmd reads a script in the console's code page, line by line, so the first line switches it to UTF-8 for the rest.
    // Windows PowerShell 5.1 reads a file without a byte order mark as ANSI.
    public static byte[] ScriptFile(RunScriptStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        string text = step.Script.ReplaceLineEndings("\r\n");

        if (!text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            text += "\r\n";
        }

        return step.Interpreter == ScriptInterpreter.PowerShell
            ? [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(text)]
            : Encoding.UTF8.GetBytes($"@chcp 65001 >nul\r\n{text}");
    }

    public async Task<StepResult> RunAsync(RunScriptStep step, StepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        bool powerShell = step.Interpreter == ScriptInterpreter.PowerShell;

        if (powerShell && context.Phase == SequencePhase.WindowsPE && !File.Exists(PowerShellPath))
        {
            return StepResult.Failed(NoPowerShellMessage);
        }

        string directory = session.RunDirectory ?? workDirectory;
        string scripts = Path.Combine(directory, "scripts");
        string file = Path.Combine(scripts, step.Id.ToString("D") + (powerShell ? ".ps1" : ".cmd"));
        Directory.CreateDirectory(scripts);
        await File.WriteAllBytesAsync(file, ScriptFile(step), cancellationToken).ConfigureAwait(false);

        string? package = null;

        if (step.PackageId is not null)
        {
            AgentRunPackage content = session.Run.Packages.FirstOrDefault(candidate => candidate.StepId == step.Id)
                ?? throw new DeploymentStepException("The server sent no package for this script. Assign the sequence again.");

            if (session.RunDirectory is null)
            {
                throw new DeploymentStepException("A script's package is unpacked on the partitioned disk, so this script can run only after the disk is partitioned.");
            }

            package = Path.Combine(directory, "packages", step.Id.ToString("D"));
            await downloads
                .UnpackAsync(content, Path.Combine(directory, "cache"), package, new ScaledProgress(context.Progress, 0, 10), cancellationToken)
                .ConfigureAwait(false);
        }

        Dictionary<string, string> environment = new(StringComparer.OrdinalIgnoreCase)
        {
            ["DDT_PHASE"] = context.Phase == SequencePhase.Windows ? nameof(SequencePhase.Windows) : nameof(SequencePhase.WindowsPE),
            ["DDT_RUN_ID"] = context.RunId.ToString("D"),
            ["DDT_STEP_ID"] = step.Id.ToString("D"),
        };

        if (package is not null)
        {
            environment["DDT_PACKAGE"] = package;
        }

        // Offline changes to the applied Windows need to know which letter Windows PE gave it.
        if (context.Phase == SequencePhase.WindowsPE && session.Volumes is { } volumes)
        {
            environment["DDT_WINDOWS"] = volumes.Windows;
        }

        string program = powerShell ? PowerShellPath : CmdPath;
        string[] arguments = powerShell
            ? ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", file]
            : ["/d", "/c", file];
        ToolRunOptions options = new(package ?? scripts, environment, TimeSpan.FromMinutes(step.TimeoutMinutes));
        int exitCode;

        try
        {
            exitCode = await tools.RunForExitCodeAsync(program, arguments, options, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (package is not null)
            {
                Leftovers.Delete(package, log);
            }
        }

        if (step.RebootExitCodes.Contains(exitCode))
        {
            log.Information($"The script ended with exit code {exitCode}, which asks for a restart before the next step.");

            return StepResult.RebootRequired();
        }

        if (step.SuccessExitCodes.Contains(exitCode))
        {
            context.Progress.Report(100);

            return StepResult.Done();
        }

        return StepResult.Failed($"The script ended with exit code {exitCode}, which is not one of its success codes ({string.Join(", ", step.SuccessExitCodes)}).");
    }
}
