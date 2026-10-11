// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Runs a sequence author's script through the system's launcher, in either phase. It runs from the run's directory,
// or from workDirectory before Partition made one. Values reach the script as DDT_VAR_<Name> in its environment, never
// in its text. The script sets variables through the file DDT_VARIABLES_OUT names. The log gets their names, never
// their values.
public sealed class RunScriptStepRunner(
    IToolRunner tools,
    IScriptLauncher launcher,
    RunDownloads downloads,
    RunSession session,
    AgentLog log,
    string workDirectory) : IStepKindRunner
{
    public const string VariablePrefix = "DDT_VAR_";

    public const string VariablesOut = "DDT_VARIABLES_OUT";

    // The device of the disk the raw disk image was written to, such as /dev/nvme0n1, once it is written.
    public const string Disk = "DDT_DISK";

    // DDT_SHARE_1 is where the script finds its first share, when the system gave the share a place of its own.
    public const string SharePrefix = "DDT_SHARE_";

    public const int MaxOutputLines = 64;

    public const int MaxOutputLineLength = 1024;

    // Blank lines count too, so a file of nothing but line ends is read no further than this.
    private const int MaxOutputCharacters = MaxOutputLines * (MaxOutputLineLength + 2) * 2;

    // The run's values with what steps set on top, without the agent's own. A name with anything but letters, digits
    // and underscores can't be an environment variable's name, and a value with a NUL can't be its value. The validator
    // lets neither through.
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

    public Task<StepResult> RunAsync(RunScriptStep step, StepContext context, CancellationToken cancellationToken) =>
        RunAsync(step, context, StepResources.None, cancellationToken);

    // A script run as an account gets a separate directory. The account is let into it and the package, because only
    // SYSTEM may open the run's directory.
    public async Task<StepResult> RunAsync(RunScriptStep step, StepContext context, StepResources resources, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resources);

        IAccountSession? account = resources.Account;

        if (launcher.Missing(step, context.Phase) is { } missing)
        {
            return StepResult.Failed(missing);
        }

        string directory = session.RunDirectory ?? workDirectory;
        string scripts = Path.Combine(directory, StepFiles.ScriptsFolder);

        if (account is not null)
        {
            scripts = Path.Combine(scripts, step.Id.ToString("D"));
        }

        Directory.CreateDirectory(scripts);

        // Before the files are written, so they inherit the entry.
        account?.Admit(scripts);
        ScriptLaunch launch = await launcher.WriteAsync(step, scripts, cancellationToken).ConfigureAwait(false);
        string? package = await UnpackAsync(step, directory, context, account, cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> environment = EnvironmentFor(step, context, package, resources.SharePaths);
        AddMissing(environment, launch.Environment);

        VariableDeclaration[] settable = [.. session.Run.Sequence.Variables?.Where(variable => variable is { SetBySteps: true }) ?? []];
        string? outputs = PrepareOutputs(step, scripts, settable, environment);
        ToolRunOptions options = new(package ?? scripts, environment, TimeSpan.FromMinutes(step.TimeoutMinutes), account);
        int exitCode;
        IReadOnlyDictionary<string, string>? set = null;

        try
        {
            exitCode = await tools.RunForExitCodeAsync(launch.FileName, launch.Arguments, options, cancellationToken).ConfigureAwait(false);

            if (outputs is not null && Succeeded(step, exitCode))
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

            // The script may have set a secret there
            if (outputs is not null)
            {
                Leftovers.Wipe(outputs, log);
            }
        }

        return ResultOf(step, context, exitCode, set);
    }

    bool IStepKindRunner.Runs(SequenceStep step) => step is RunScriptStep;

    Task<StepResult> IStepKindRunner.RunAsync(SequenceStep step, StepContext context, StepResources resources, CancellationToken cancellationToken) =>
        RunAsync((RunScriptStep)step, context, resources, cancellationToken);

    private static bool Succeeded(RunScriptStep step, int exitCode) =>
        step.SuccessExitCodes.Contains(exitCode) || step.RebootExitCodes.Contains(exitCode);

    // The step's package, unpacked on the partitioned disk to be the script's working directory. Null without a
    // package.
    private async Task<string?> UnpackAsync(
        RunScriptStep step,
        string directory,
        StepContext context,
        IAccountSession? account,
        CancellationToken cancellationToken)
    {
        if (step.PackageId is null)
        {
            return null;
        }

        AgentRunPackage content = session.Run.Packages.FirstOrDefault(candidate => candidate.StepId == step.Id)
            ?? throw new DeploymentStepException("The server sent no package for this script. Assign the sequence again.");

        if (session.RunDirectory is null)
        {
            throw new DeploymentStepException(NoPlaceForPackage(context.Phase));
        }

        string package = Path.Combine(directory, StepFiles.PackagesFolder, step.Id.ToString("D"));
        await downloads
            .UnpackAsync(content, Path.Combine(directory, StepFiles.CacheFolder), package, new ScaledProgress(context.Progress, 0, 10), cancellationToken)
            .ConfigureAwait(false);
        launcher.Unpacked(package);
        account?.Admit(package);

        return package;
    }

    // Why a run without a directory on a disk cannot unpack a package in this phase.
    public static string NoPlaceForPackage(SequencePhase phase) => phase switch
    {
        SequencePhase.LinuxBoot => "The Linux boot environment has no partition to unpack a package on, so this script cannot have one.",
        SequencePhase.Linux => "The run has no directory in the installed Linux to unpack a package in, so this script cannot have one.",
        _ => "A script's package is unpacked on the partitioned disk, so this script can run only after the disk is partitioned.",
    };

    // Only a sequence with variables that steps may set has somewhere for the script's variables to go. A file left by
    // an earlier visit of the step, in a repeat or before a restart, must not count for this visit.
    private string? PrepareOutputs(
        RunScriptStep step,
        string scripts,
        IReadOnlyList<VariableDeclaration> settable,
        Dictionary<string, string> environment)
    {
        if (settable.Count == 0)
        {
            return null;
        }

        string outputs = Path.Combine(scripts, step.Id.ToString("D") + StepFiles.VariablesExtension);
        Leftovers.Wipe(outputs, log);

        // What could not be removed would count for this visit, so the step fails on it here
        File.Delete(outputs);
        environment[VariablesOut] = outputs;

        return outputs;
    }

    private Dictionary<string, string> EnvironmentFor(RunScriptStep step, StepContext context, string? package, IReadOnlyList<string> sharePaths)
    {
        Dictionary<string, string> environment = new(StringComparer.OrdinalIgnoreCase)
        {
            ["DDT_PHASE"] = context.Phase.ToString(),
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

        // A script after the image changes what is on the disk, so it has to know which one it is.
        if (session.Disk?.Device is { } device && context.Variables.TryGetValue(RunVariables.RawImageWritten, out string? written) && written == RunVariables.Set)
        {
            environment[Disk] = device;
        }

        for (int index = 0; index < sharePaths.Count; index++)
        {
            environment[string.Create(CultureInfo.InvariantCulture, $"{SharePrefix}{index + 1}")] = sharePaths[index];
        }

        foreach ((string name, string value) in UserVariables(context))
        {
            environment[VariablePrefix + name] = value;
        }

        return environment;
    }

    // The launcher's variables never replace DDT's.
    private static void AddMissing(Dictionary<string, string> environment, IReadOnlyDictionary<string, string>? more)
    {
        foreach ((string name, string value) in more ?? new Dictionary<string, string>())
        {
            environment.TryAdd(name, value);
        }
    }

    // A restart code restarts the machine before the next step, a success code is done, and any other code fails.
    private StepResult ResultOf(RunScriptStep step, StepContext context, int exitCode, IReadOnlyDictionary<string, string>? set)
    {
        if (step.RebootExitCodes.Contains(exitCode))
        {
            log.Information($"The script ended with exit code {exitCode}, which asks for a restart before the next step.");

            return StepResult.RebootRequired(set) with { ExitCode = exitCode };
        }

        if (Succeeded(step, exitCode))
        {
            context.Progress.Report(100);

            return StepResult.Done(set) with { ExitCode = exitCode };
        }

        string error = $"The script ended with exit code {exitCode}, which is not one of its success codes ({string.Join(", ", step.SuccessExitCodes)}).";

        return StepResult.Failed(error) with { ExitCode = exitCode };
    }

    // Reads Name=Value lines in UTF-8, or UTF-16 when a byte order mark says so, which Windows PowerShell's Out-File
    // writes. It takes at most MaxOutputLines lines of MaxOutputLineLength characters, and only names the sequence
    // lets steps set.
    private async Task<IReadOnlyDictionary<string, string>?> ReadOutputsAsync(
        string path,
        IReadOnlyList<VariableDeclaration> settable,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        (string text, bool cut) = await ReadCappedAsync(path, cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> set = new(StringComparer.Ordinal);
        List<string> dropped = [];
        int taken = TakeLines(text.Split('\n'), settable, set, dropped);

        if (cut && taken <= MaxOutputLines)
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

    // Cut is true when the file holds more than MaxOutputCharacters.
    private static async Task<(string Text, bool Cut)> ReadCappedAsync(string path, CancellationToken cancellationToken)
    {
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

        return (new string(buffer, 0, Math.Min(read, MaxOutputCharacters)), read > MaxOutputCharacters);
    }

    // Blank lines are skipped, and a name and its value are trimmed, because cmd's echo leaves a space before >. A name
    // is stored in the sequence's spelling. Returns how many lines were taken.
    private int TakeLines(string[] lines, IReadOnlyList<VariableDeclaration> settable, Dictionary<string, string> set, List<string> dropped)
    {
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

        return taken;
    }

    // A name as the log shows it, cut short, because a line without its = could be anything.
    private static string Named(string name) => name.Length > 64 ? $"{name[..64]}..." : name;
}
