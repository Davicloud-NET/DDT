// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Contracts.Sequences;

namespace DDT.Agent.Sequences;

// Runs a script with cmd or Windows PowerShell. A PowerShell script starts from a cmd launcher next to it.
public sealed class WindowsScriptLauncher : IScriptLauncher
{
    public const string NoPowerShellMessage =
        "This boot image has no Windows PowerShell, which a script of this sequence needs in Windows PE. Build the boot image " +
        "again with build\\Build-BootImage.ps1 without -SkipPowerShell.";

    public const string ShellScriptMessage =
        "Windows runs cmd and PowerShell scripts, and this script was written for the shell. Choose cmd or PowerShell for it in the sequence.";

    public static string CmdPath => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    public static string PowerShellPath => PowerShellIn(Environment.SystemDirectory);

    public static string PowerShellIn(string systemDirectory) => Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    // Windows PowerShell 5.1 reads a file without a byte order mark as ANSI.
    public static byte[] ScriptFile(RunScriptStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        return step.Interpreter == ScriptInterpreter.PowerShell
            ? [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(Lines(step.Script))]
            : CmdFile(step.Script);
    }

    // cmd reads a script in the console's code page, line by line, so the first line switches it to UTF-8 for the rest.
    public static byte[] CmdFile(string script) => Encoding.UTF8.GetBytes($"@chcp 65001 >nul\r\n{Lines(script)}");

    // PowerShell and the programs it starts write in the console's code page, so a cmd file switches the console to
    // UTF-8 first and passes PowerShell's exit code on. cmd would expand a % in a path.
    public static byte[] PowerShellLauncher(string powerShell, string script) => Encoding.UTF8.GetBytes(
        $"@chcp 65001 >nul\r\n@\"{Escape(powerShell)}\" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{Escape(script)}\"\r\n");

    // The installed Windows brings its own PowerShell. A lean boot image may lack it. No Windows runs a shell script.
    public string? Missing(RunScriptStep step, SequencePhase phase)
    {
        ArgumentNullException.ThrowIfNull(step);

        if (step.Interpreter == ScriptInterpreter.Shell)
        {
            return ShellScriptMessage;
        }

        return step.Interpreter == ScriptInterpreter.PowerShell && phase == SequencePhase.WindowsPE && !File.Exists(PowerShellPath)
            ? NoPowerShellMessage
            : null;
    }

    public async Task<ScriptLaunch> WriteAsync(RunScriptStep step, string directory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);

        bool powerShell = step.Interpreter == ScriptInterpreter.PowerShell;
        string file = Path.Combine(directory, step.Id.ToString("D") + (powerShell ? ".ps1" : ".cmd"));
        await File.WriteAllBytesAsync(file, ScriptFile(step), cancellationToken).ConfigureAwait(false);

        if (powerShell)
        {
            string launcher = Path.ChangeExtension(file, ".cmd");
            await File.WriteAllBytesAsync(launcher, PowerShellLauncher(PowerShellPath, file), cancellationToken).ConfigureAwait(false);
            file = launcher;
        }

        return new ScriptLaunch(CmdPath, ["/d", "/c", file]);
    }

    private static string Escape(string path) => path.Replace("%", "%%", StringComparison.Ordinal);

    private static string Lines(string script)
    {
        string text = script.ReplaceLineEndings("\r\n");

        return text.EndsWith("\r\n", StringComparison.Ordinal) ? text : text + "\r\n";
    }
}
