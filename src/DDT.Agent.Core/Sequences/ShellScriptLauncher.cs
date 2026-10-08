// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Contracts.Sequences;

namespace DDT.Agent.Sequences;

// Runs a script with the interpreter its #! line names, or else with /bin/sh, which gets the file as an argument.
public sealed class ShellScriptLauncher(bool dryRun = false) : IScriptLauncher
{
    public const string DefaultShell = "/bin/sh";

    public const string NotAShellScriptMessage =
        "Linux runs shell scripts, and this script was written for cmd or PowerShell. Choose the shell for it in the sequence.";

    private const string Marker = "#!";

    private const char ByteOrderMark = '\uFEFF';

    // The script as its file holds it. A byte order mark or a carriage return would end up in the first command, or in
    // the interpreter's name. Only CR LF and CR become LF: a shell takes every other line separator as text.
    public static string Text(string script)
    {
        ArgumentNullException.ThrowIfNull(script);

        string text = (script.StartsWith(ByteOrderMark) ? script[1..] : script)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        return text.EndsWith('\n') ? text : text + "\n";
    }

    // The interpreter of a first line such as "#!/usr/bin/env bash", and its argument if it has one. Like the kernel,
    // this takes everything after the interpreter as one argument. Null without such a line.
    public static IReadOnlyList<string>? Interpreter(string script) => Named(Text(script));

    // The file is its owner's alone from the moment it exists, where the system has such modes. The script may hold
    // what only root should read.
    public static FileStreamOptions NewFile()
    {
        FileStreamOptions options = new() { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        }

        return options;
    }

    // A dry run starts nothing, so it asks for no interpreter.
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
        string text = Text(step.Script);

        // A file from an earlier turn of the step would keep the mode it has.
        File.Delete(file);

        await using (FileStream stream = new(file, NewFile()))
        {
            await stream.WriteAsync(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text), cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<string> interpreter = Named(text) ?? [DefaultShell];

        return new ScriptLaunch(interpreter[0], [.. interpreter.Skip(1), file]);
    }

    // The kernel ends the line at LF alone and splits it at spaces and tabs alone.
    private static string[]? Named(string text)
    {
        if (!text.StartsWith(Marker, StringComparison.Ordinal))
        {
            return null;
        }

        string line = text[Marker.Length..text.IndexOf('\n', StringComparison.Ordinal)].Trim(' ', '\t');
        int space = line.IndexOfAny([' ', '\t']);

        if (line.Length == 0)
        {
            return null;
        }

        return space < 0 ? [line] : [line[..space], line[space..].Trim(' ', '\t')];
    }
}
