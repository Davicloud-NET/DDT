// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// What makes a sequence runnable. The server stores a sequence with problems as a draft but never runs it, and the
// agent checks again before it starts. The document comes from outside, so members declared non-null can be null.
public static class SequenceValidator
{
    public const int MaxSteps = 100;
    public const int MaxNameLength = 100;
    public const int MaxConditions = 10;
    public const int MaxScriptBytes = 64 * 1024;
    public const int MaxTimeoutMinutes = 24 * 60;
    public const int MaxExitCodes = 16;
    public const int MaxSeedFileBytes = 64 * 1024;

    // Microsoft's minimums: 260 MB for the system partition on 4K native disks, 300 MB for a recovery partition.
    // The upper limits only catch typing errors, which would otherwise fail after the disk was cleaned.
    private const int MinSystemPartitionMegabytes = 260;
    private const int MaxSystemPartitionMegabytes = 4096;
    private const int MinRecoveryPartitionMegabytes = 300;
    private const int MaxRecoveryPartitionMegabytes = 65536;

    // Before Partition the run's state exists only in memory, so a restart in Windows PE would lose the run.
    private const string RestartBeforePartition =
        "Windows PE can restart only after the disk is partitioned, because the run's state is kept on the disk.";

    // A raw disk image leaves no partition DDT could keep the run's state or unpack a package on.
    private const string RestartWithRawImage =
        "A sequence that writes a raw disk image keeps its state in memory, so Windows PE cannot restart during it.";

    private const string PackageWithRawImage =
        "A sequence that writes a raw disk image has no partition to unpack a package on, so its scripts cannot have one.";

    private const string WindowsWithRawImage =
        "A sequence either installs Windows or writes a raw disk image. This step belongs to installing Windows.";

    public static IReadOnlyList<SequenceProblem> Validate(SequenceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        List<SequenceProblem> problems = [];

        if (definition.Version is < 1 or > SequenceDefinition.CurrentVersion)
        {
            problems.Add(new SequenceProblem(
                null,
                "version",
                $"This version of DDT runs sequences of version 1 to {SequenceDefinition.CurrentVersion}, not {definition.Version}."));
        }
        else if (definition.Version < definition.RequiredVersion())
        {
            problems.Add(new SequenceProblem(
                null,
                "version",
                $"The sequence has steps of version {definition.RequiredVersion()}, but says it is of version {definition.Version}."));
        }

        IReadOnlyList<SequenceStep?> steps = definition.Steps ?? [];

        if (steps.Count is 0 or > MaxSteps)
        {
            problems.Add(new SequenceProblem(null, "steps", $"A sequence needs 1 to {MaxSteps} steps."));
        }

        if (steps.Contains(null))
        {
            problems.Add(new SequenceProblem(null, "steps", "A step is empty."));

            return problems;
        }

        HashSet<Guid> ids = [];
        bool partitioned = false;
        bool imageApplied = false;
        bool imageAppliedEveryTime = false;
        bool inWindows = false;
        bool unattendWritten = false;
        bool domainJoined = false;
        bool writesRawImage = steps.Any(step => step is WriteRawImageStep);
        bool rawImageWritten = false;
        bool seedWritten = false;
        string noRestart = writesRawImage ? RestartWithRawImage : RestartBeforePartition;

        for (int index = 0; index < steps.Count; index++)
        {
            SequenceStep step = steps[index]!;
            SequencePhase phase = SequencePhases.Of(definition, index);
            string? phaseField = step is RunScriptStep ? "phase" : null;

            void Add(string? field, string message) =>
                problems.Add(new SequenceProblem(step.Id == Guid.Empty ? null : step.Id, field, message));

            if (step.Id == Guid.Empty)
            {
                Add("id", $"Step {index + 1} has no id.");
            }
            else if (!ids.Add(step.Id))
            {
                Add("id", $"Step {index + 1} has the id of an earlier step.");
            }

            if (string.IsNullOrWhiteSpace(step.Name))
            {
                Add("name", "Enter a name for the step.");
            }
            else if (step.Name.Length > MaxNameLength)
            {
                Add("name", $"A step name can have at most {MaxNameLength} characters.");
            }

            CheckConditions(step.Conditions, Add);

            if (phase == SequencePhase.WindowsPE && inWindows)
            {
                Add(phaseField, "Steps in Windows PE come first, and an earlier step already runs in Windows.");
            }

            if (step.RequiredPhase == SequencePhase.Windows && !imageAppliedEveryTime && !writesRawImage)
            {
                Add(phaseField, "A step in Windows needs an earlier step that applies the image without conditions.");
            }

            if (step.RebootAfter && step is RebootStep)
            {
                Add("rebootAfter", "A restart step restarts anyway, so it cannot restart again after it.");
            }
            else if (step.RebootAfter && phase == SequencePhase.WindowsPE && !partitioned && step is not PartitionStep)
            {
                Add("rebootAfter", noRestart);
            }

            // The rules of a Windows installation would only repeat what is wrong in other words.
            if (writesRawImage && (step is PartitionStep or ApplyImageStep or InjectDriversStep or WriteUnattendStep or JoinDomainStep
                || step.RequiredPhase == SequencePhase.Windows))
            {
                Add(step is RunScriptStep ? "phase" : null, WindowsWithRawImage);
                inWindows |= phase == SequencePhase.Windows;

                continue;
            }

            switch (step)
            {
                case PartitionStep partition:
                    CheckPartition(partition, partitioned, imageApplied, Add);
                    partitioned = true;
                    break;
                case ApplyImageStep:
                    if (imageApplied)
                    {
                        Add(null, "A sequence can apply only one image.");
                    }

                    if (!partitioned)
                    {
                        Add(null, "The image can be applied only after a step that partitions the disk.");
                    }

                    CheckRunsEveryTime(step, "applying the image", Add);
                    imageApplied = true;
                    imageAppliedEveryTime |= step.Conditions is { Count: 0 };
                    break;
                case InjectDriversStep:
                    if (!imageApplied)
                    {
                        Add(null, "Drivers can be added only after the image is applied.");
                    }

                    break;
                case WriteUnattendStep:
                    if (!imageApplied)
                    {
                        Add(null, "The answer file can be written only after the image is applied.");
                    }

                    if (unattendWritten)
                    {
                        Add(null, "A sequence can write the answer file only once.");
                    }

                    unattendWritten = true;
                    break;
                case JoinDomainStep:
                    if (domainJoined)
                    {
                        Add(null, "A sequence can join the domain only once.");
                    }

                    domainJoined = true;
                    break;
                case RunScriptStep script:
                    CheckScript(script, phase == SequencePhase.WindowsPE && !partitioned, writesRawImage, Add);
                    break;
                case RebootStep:
                    if (phase == SequencePhase.WindowsPE && !partitioned)
                    {
                        Add(null, noRestart);
                    }

                    break;
                case WriteRawImageStep:
                    if (rawImageWritten)
                    {
                        Add(null, "A sequence can write only one raw disk image.");
                    }

                    CheckRunsEveryTime(step, "writing the raw disk image", Add);
                    rawImageWritten = true;
                    break;
                case WriteCloudInitSeedStep seed:
                    if (!rawImageWritten)
                    {
                        Add(null, "The cloud-init seed can be written only after a step that writes a raw disk image.");
                    }

                    if (seedWritten)
                    {
                        Add(null, "A sequence can write the cloud-init seed only once.");
                    }

                    CheckSeed(seed, Add);
                    seedWritten = true;
                    break;
                default:
                    Add("kind", "This version of DDT does not know this kind of step.");
                    break;
            }

            inWindows |= phase == SequencePhase.Windows;
        }

        return problems;
    }

    private static void CheckConditions(IReadOnlyList<StepCondition?>? conditions, Action<string?, string> add)
    {
        if (conditions is null)
        {
            add("conditions", "The conditions are missing.");

            return;
        }

        if (conditions.Count > MaxConditions)
        {
            add("conditions", $"A step can have at most {MaxConditions} conditions.");
        }

        for (int index = 0; index < conditions.Count; index++)
        {
            string field = $"conditions[{index}]";

            if (conditions[index] is not { } condition)
            {
                add(field, "The condition is empty.");

                continue;
            }

            if (!MachineVariableNames.All.Contains(condition.Variable, StringComparer.Ordinal))
            {
                add($"{field}.variable", $"Choose one of the variables {string.Join(", ", MachineVariableNames.All)}.");
            }

            if (!Enum.IsDefined(condition.Operator))
            {
                add($"{field}.operator", "Choose an operator.");
            }

            if (string.IsNullOrWhiteSpace(condition.Value))
            {
                add($"{field}.value", "Enter the value to compare with.");
            }
            else if (condition.Variable == MachineVariableNames.MacAddress)
            {
                CheckMac(condition, $"{field}.value", add);
            }
        }
    }

    // Separators alone would leave nothing to compare, and StartsWith or Contains would then hold on every machine.
    private static void CheckMac(StepCondition condition, string field, Action<string?, string> add)
    {
        string digits = ConditionEvaluator.NormaliseMac(condition.Value);
        bool hex = digits.All(char.IsAsciiHexDigit);

        if (condition.Operator is ConditionOperator.Equals or ConditionOperator.NotEquals)
        {
            if (digits.Length != 12 || !hex)
            {
                add(field, "Enter a MAC address of 12 hex digits, such as 00:15:5D:01:02:03.");
            }
        }
        else if (digits.Length is 0 or > 12 || !hex)
        {
            add(field, "Enter 1 to 12 hex digits of a MAC address, such as 00:15:5D.");
        }
    }

    private static void CheckPartition(PartitionStep partition, bool partitioned, bool imageApplied, Action<string?, string> add)
    {
        if (partitioned)
        {
            add(null, "A sequence can partition the disk only once.");
        }

        if (imageApplied)
        {
            add(null, "The disk has to be partitioned before the image is applied.");
        }

        CheckRunsEveryTime(partition, "partitioning the disk", add);

        if (partition.SystemPartitionMegabytes is < MinSystemPartitionMegabytes or > MaxSystemPartitionMegabytes)
        {
            add(
                "systemPartitionMegabytes",
                $"The system partition needs {MinSystemPartitionMegabytes} to {MaxSystemPartitionMegabytes} MB.");
        }

        if (partition.RecoveryPartitionMegabytes is < MinRecoveryPartitionMegabytes or > MaxRecoveryPartitionMegabytes)
        {
            add(
                "recoveryPartitionMegabytes",
                $"The recovery partition needs {MinRecoveryPartitionMegabytes} to {MaxRecoveryPartitionMegabytes} MB.");
        }
    }

    // Later steps rely on these, so they can neither be skipped nor fail quietly.
    private static void CheckRunsEveryTime(SequenceStep step, string activity, Action<string?, string> add)
    {
        if (step.Conditions is { Count: > 0 })
        {
            add("conditions", $"The sequence cannot skip {activity}, so this step cannot have conditions.");
        }

        if (step.ContinueOnError)
        {
            add("continueOnError", $"The sequence cannot go on when {activity} fails.");
        }
    }

    private static void CheckSeed(WriteCloudInitSeedStep seed, Action<string?, string> add)
    {
        foreach ((string field, string file, string? text, bool required) in new[]
        {
            ("metaData", "meta-data", seed.MetaData, true),
            ("userData", "user-data", seed.UserData, true),
            ("networkConfig", "network-config", seed.NetworkConfig, false),
        })
        {
            if (text is null && required)
            {
                add(field, $"The {file} file is missing. It can be empty.");
            }
            else if (text is not null && Encoding.UTF8.GetByteCount(text) > MaxSeedFileBytes)
            {
                add(field, $"The {file} file can have at most {MaxSeedFileBytes / 1024} KiB.");
            }
        }
    }

    private static void CheckScript(RunScriptStep script, bool beforePartitionInWindowsPE, bool writesRawImage, Action<string?, string> add)
    {
        if (!Enum.IsDefined(script.Phase))
        {
            add("phase", "Choose Windows PE or Windows.");
        }

        if (!Enum.IsDefined(script.Interpreter))
        {
            add("interpreter", "Choose cmd or PowerShell.");
        }

        if (string.IsNullOrWhiteSpace(script.Script))
        {
            add("script", "Enter the script.");
        }
        else if (Encoding.UTF8.GetByteCount(script.Script) > MaxScriptBytes)
        {
            add("script", $"A script can have at most {MaxScriptBytes / 1024} KiB.");
        }

        if (script.TimeoutMinutes is < 1 or > MaxTimeoutMinutes)
        {
            add("timeoutMinutes", $"The timeout has to be 1 to {MaxTimeoutMinutes} minutes.");
        }

        if (script.SuccessExitCodes is not { Count: >= 1 and <= MaxExitCodes })
        {
            add("successExitCodes", $"Enter 1 to {MaxExitCodes} exit codes that mean success.");
        }

        // No restart codes is allowed: a script whose 3010 means success lists it among the success codes instead.
        if (script.RebootExitCodes is not { Count: <= MaxExitCodes })
        {
            add("rebootExitCodes", $"Enter at most {MaxExitCodes} exit codes that ask for a restart.");
        }
        else if (script.SuccessExitCodes is not null
            && script.SuccessExitCodes.Intersect(script.RebootExitCodes).ToArray() is { Length: > 0 } both)
        {
            add("rebootExitCodes", $"An exit code cannot mean both success and a restart: {string.Join(", ", both)}.");
        }

        if (beforePartitionInWindowsPE && script.PackageId is not null)
        {
            add(
                "packageId",
                writesRawImage
                    ? PackageWithRawImage
                    : "In Windows PE, a script with a package runs only after the disk is partitioned, where the package is put.");
        }

        if (beforePartitionInWindowsPE && script.RebootExitCodes is { Count: > 0 })
        {
            add("rebootExitCodes", writesRawImage ? RestartWithRawImage : RestartBeforePartition);
        }
    }
}
