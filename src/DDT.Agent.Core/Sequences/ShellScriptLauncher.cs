// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Contracts.Sequences;

namespace DDT.Agent.Sequences;

// Runs a script with the interpreter its #! line names, or else with /bin/sh. The interpreter gets the file, so the
// script runs from a volume mounted noexec too. A dry run starts nothing, so it asks for no interpreter.
public sealed class ShellScriptLauncher(bool dryRun = false) : IScriptLauncher
{
    public const string DefaultShell = "/bin/sh";

    public const string NotAShellScriptMessage =
        "Linux runs shell scripts, and this script was written for cmd or PowerShell. Choose the shell for it in the sequence.";

    private const string Marker = "#!";

    // LF line ends and no byte order mark, which the kernel and the shell would take for part of the first line.
    public static byte[] ScriptFile(RunScriptStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        string text = step.Script.ReplaceLineEndings("\n");

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text.EndsWith('\n') ? text : text + "\n");
    }

    // The interpreter of a first line such as "#!/usr/bin/env bash", and its argument if it has one. Like the kernel,
    // this takes everything after the interpreter as one argument. Null without such a line.
    public static IReadOnlyList<string>? Interpreter(string script)
    {
        ArgumentNullException.ThrowIfNull(script);

        if (!script.StartsWith(Marker, StringComparison.Ordinal))
        {
            return null;
        }

        string line = script[Marker.Length..].Split('\n', 2)[0].Trim();
        int space = line.IndexOfAny([' ', '\t']);

        if (line.Length == 0)
        {
            return null;
        }

        return space < 0 ? [line] : [line[..space], line[space..].Trim()];
    }

    public string? Missing(RunScriptStep step, SequencePhase phase)
    {
        ArgumentNullException.ThrowIfNull(step);

        if (step.Interpreter != ScriptInterpreter.Shell)
        {
            return NotAShellScriptMessage;
        }

        IReadOnlyList<string>? named = Interpreter(step.Script);
        string interpreter = named?[0] ?? DefaultShell;

        if (dryRun || File.Exists(interpreter))
        {
            return null;
        }

        return named is null
            ? $"This system has no {DefaultShell} to run the script with."
            : $"This system has no {interpreter}, which the script's first line names. Name an interpreter it has, or leave the line out for {DefaultShell}.";
    }

    public async Task<ScriptLaunch> WriteAsync(RunScriptStep step, string directory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);

        string file = Path.Combine(directory, step.Id.ToString("D") + ".sh");
        await File.WriteAllBytesAsync(file, ScriptFile(step), cancellationToken).ConfigureAwait(false);

        // The script may hold what only root should read.
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        IReadOnlyList<string> interpreter = Interpreter(step.Script) ?? [DefaultShell];

        return new ScriptLaunch(interpreter[0], [.. interpreter.Skip(1), file]);
    }
}
