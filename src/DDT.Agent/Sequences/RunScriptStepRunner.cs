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
// decides: a restart code restarts the machine before the next step, a success code is done, any other fails. A script
// that runs as an account, in Windows only, gets a directory of its own under scripts, which the account's logon session
// is let into together with the package, as the run's directory is open to SYSTEM alone.
//
// A script reads the run's values and what steps set so far as DDT_VAR_<Name>, in its environment and never in its text,
// and may set the sequence's variables that steps may set by writing Name=Value lines to the file DDT_VARIABLES_OUT
// names. None of them is a secret, and still no value reaches the log: only names do.
public sealed class RunScriptStepRunner(IToolRunner tools, RunDownloads downloads, RunSession session, AgentLog log, string workDirectory)
{
    public const string VariablePrefix = "DDT_VAR_";

    public const string VariablesOut = "DDT_VARIABLES_OUT";

    public const int MaxOutputLines = 64;

    public const int MaxOutputLineLength = 1024;

    // Blank lines count too, so a file of nothing but line ends is read no further than this.
    private const int MaxOutputCharacters = MaxOutputLines * (MaxOutputLineLength + 2) * 2;

    public const string NoPowerShellMessage =
        "This boot image has no Windows PowerShell, which a script of this sequence needs in Windows PE. Build the boot image " +
        "again with build\\Build-BootImage.ps1 without -SkipPowerShell.";

    public static string CmdPath => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    public static string PowerShellPath => PowerShellIn(Environment.SystemDirectory);

    public static string PowerShellIn(string systemDirectory) => Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

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

    // Windows PowerShell and the programs a script starts write in the console's code page, which is not UTF-8, so a
    // PowerShell script starts from a cmd file that switches the console to UTF-8 first, as a cmd script does itself.
    // PowerShell's exit code is the file's. cmd would expand a % in a path.
    public static byte[] PowerShellLauncher(string powerShell, string script) => Encoding.UTF8.GetBytes(
        $"@chcp 65001 >nul\r\n@\"{Escape(powerShell)}\" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{Escape(script)}\"\r\n");

    private static string Escape(string path) => path.Replace("%", "%%", StringComparison.Ordinal);

    public Task<StepResult> RunAsync(RunScriptStep step, StepContext context, CancellationToken cancellationToken) =>
        RunAsync(step, context, null, cancellationToken);

    // account is the account the step runs as, signed in already, or null to run as the agent.
    public async Task<StepResult> RunAsync(RunScriptStep step, StepContext context, IAccountSession? account, CancellationToken cancellationToken)
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

        if (account is not null)
        {
            scripts = Path.Combine(scripts, step.Id.ToString("D"));
        }

        string file = Path.Combine(scripts, step.Id.ToString("D") + (powerShell ? ".ps1" : ".cmd"));
        Directory.CreateDirectory(scripts);

        // Before the files are written, so they inherit the entry.
        account?.Admit(scripts);
        await File.WriteAllBytesAsync(file, ScriptFile(step), cancellationToken).ConfigureAwait(false);
        string launcher = file;

        if (powerShell)
        {
            launcher = Path.ChangeExtension(file, ".cmd");
            await File.WriteAllBytesAsync(launcher, PowerShellLauncher(PowerShellPath, file), cancellationToken).ConfigureAwait(false);
        }

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
            account?.Admit(package);
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

        foreach ((string name, string value) in UserVariables(context))
        {
            environment[VariablePrefix + name] = value;
        }

        // Only a sequence with variables steps may set has somewhere for the script's to go. A file an earlier visit of
        // the step left, in a repeat or before a restart, must not count as this one's.
        VariableDeclaration[] settable = [.. session.Run.Sequence.Variables?.Where(variable => variable is { SetBySteps: true }) ?? []];
        string? outputs = settable.Length > 0 ? Path.Combine(scripts, step.Id.ToString("D") + ".variables") : null;

        if (outputs is not null)
        {
            File.Delete(outputs);
            environment[VariablesOut] = outputs;
        }

        ToolRunOptions options = new(package ?? scripts, environment, TimeSpan.FromMinutes(step.TimeoutMinutes), account);
        bool succeeded;
        int exitCode;
        IReadOnlyDictionary<string, string>? set = null;

        try
        {
            exitCode = await tools.RunForExitCodeAsync(CmdPath, ["/d", "/c", launcher], options, cancellationToken).ConfigureAwait(false);
            succeeded = step.SuccessExitCodes.Contains(exitCode) || step.RebootExitCodes.Contains(exitCode);

            if (outputs is not null && succeeded)
            {
                set = await ReadOutputsAsync(outputs, settable, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (package is not null)
            {
                Leftovers.Delete(package, log);
            }

            if (outputs is not null)
            {
                Leftovers.Delete(outputs, log);
            }
        }

        if (step.RebootExitCodes.Contains(exitCode))
        {
            log.Information($"The script ended with exit code {exitCode}, which asks for a restart before the next step.");

            return StepResult.RebootRequired(set) with { ExitCode = exitCode };
        }

        if (succeeded)
        {
            context.Progress.Report(100);

            return StepResult.Done(set) with { ExitCode = exitCode };
        }

        string error = $"The script ended with exit code {exitCode}, which is not one of its success codes ({string.Join(", ", step.SuccessExitCodes)}).";

        return StepResult.Failed(error) with { ExitCode = exitCode };
    }

    // The run's values, with what steps set on top, and never the agent's own variables. An environment variable's name
    // cannot hold every character a value's name may, so a name of anything but letters, digits and underscores is left
    // out, as is a value holding a NUL; the validator lets neither through.
    public static IReadOnlyDictionary<string, string> UserVariables(StepContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase);

        foreach (IReadOnlyDictionary<string, string>? source in new[] { context.Machine.Variables, context.Variables })
        {
            foreach ((string name, string value) in source ?? new Dictionary<string, string>())
            {
                if (!RunVariables.IsOwn(name) && name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') && !value.Contains('\0', StringComparison.Ordinal))
                {
                    variables[name] = value;
                }
            }
        }

        return variables;
    }

    // Name=Value lines in UTF-8, or in UTF-16 where a byte order mark says so, as Windows PowerShell's Out-File writes by
    // default. Blank lines are passed over, and a name and its value are trimmed, as cmd's echo leaves a space before >.
    // At most MaxOutputLines lines of at most MaxOutputLineLength characters are read. A name the sequence lets steps set
    // is taken in the sequence's spelling; any other is dropped with a warning that names it.
    private async Task<IReadOnlyDictionary<string, string>?> ReadOutputsAsync(
        string path,
        IReadOnlyList<VariableDeclaration> settable,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        char[] buffer = new char[MaxOutputCharacters + 1];
        int read = 0;

        using (StreamReader reader = new(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            int count;

            while (read < buffer.Length && (count = await reader.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false)) > 0)
            {
                read += count;
            }
        }

        string[] lines = new string(buffer, 0, Math.Min(read, MaxOutputCharacters)).Split('\n');
        Dictionary<string, string> set = new(StringComparer.Ordinal);
        List<string> dropped = [];
        int taken = 0;

        for (int number = 1; number <= lines.Length; number++)
        {
            string line = lines[number - 1].TrimEnd('\r');

            if (line.Trim().Length == 0)
            {
                continue;
            }

            if (++taken > MaxOutputLines)
            {
                log.Warning($"The script wrote more than {MaxOutputLines} lines to {VariablesOut}, and those after line {number - 1} were not read.");
                break;
            }

            int equals = line.IndexOf('=', StringComparison.Ordinal);
            string name = equals < 0 ? "" : line[..equals].Trim();

            if (name.Length == 0)
            {
                log.Warning($"Line {number} of {VariablesOut} is not Name=Value, so it was not read.");
            }
            else if (line.Length > MaxOutputLineLength)
            {
                log.Warning($"Line {number} of {VariablesOut}, for {Named(name)}, is longer than {MaxOutputLineLength} characters, so it was not read.");
            }
            else if (settable.FirstOrDefault(variable => string.Equals(variable.Name, name, StringComparison.OrdinalIgnoreCase)) is { } declared)
            {
                set[declared.Name] = line[(equals + 1)..].Trim();
            }
            else
            {
                dropped.Add(Named(name));
            }
        }

        if (read > MaxOutputCharacters && taken <= MaxOutputLines)
        {
            log.Warning($"{VariablesOut} holds more than {MaxOutputCharacters} characters, and what came after them was not read.");
        }

        if (dropped.Count > 0)
        {
            log.Warning(
                $"The script set {string.Join(", ", dropped.Distinct(StringComparer.OrdinalIgnoreCase))}, which the sequence does not let " +
                "steps set, so the run does not take them.");
        }

        if (set.Count > 0)
        {
            log.Information($"The script set {string.Join(", ", set.Keys)}.");
        }

        return set;
    }

    // A name as the log shows it, cut short: a line without its = could be anything.
    private static string Named(string name) => name.Length > 64 ? $"{name[..64]}..." : name;
}
