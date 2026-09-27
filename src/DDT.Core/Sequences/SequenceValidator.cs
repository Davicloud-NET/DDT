// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Contracts.Messages;
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

    public static IReadOnlyList<SequenceProblem> Validate(SequenceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        List<SequenceProblem> problems = [];

        if (definition.Version is < 1 or > SequenceDefinition.CurrentVersion)
        {
            problems.Add(SequenceProblem.From(
                null,
                "version",
                ServerMessages.SequenceVersionUnsupported.With("current", SequenceDefinition.CurrentVersion, "version", definition.Version)));
        }
        else if (definition.Version < definition.RequiredVersion())
        {
            problems.Add(SequenceProblem.From(
                null,
                "version",
                ServerMessages.SequenceVersionTooLow.With("required", definition.RequiredVersion(), "version", definition.Version)));
        }

        IReadOnlyList<SequenceStep?> steps = definition.Steps ?? [];

        if (steps.Count is 0 or > MaxSteps)
        {
            problems.Add(SequenceProblem.From(null, "steps", ServerMessages.SequenceStepCount.With("max", MaxSteps)));
        }

        if (steps.Contains(null))
        {
            problems.Add(SequenceProblem.From(null, "steps", ServerMessages.SequenceStepEmpty.With()));

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
        // Before Partition the run's state exists only in memory, so a restart in Windows PE would lose the run. A raw disk
        // image leaves no partition DDT could keep the run's state or unpack a package on.
        MessageTemplate noRestart = writesRawImage ? ServerMessages.SequenceRestartWithRawImage : ServerMessages.SequenceRestartBeforePartition;

        for (int index = 0; index < steps.Count; index++)
        {
            SequenceStep step = steps[index]!;
            SequencePhase phase = SequencePhases.Of(definition, index);
            string? phaseField = step is RunScriptStep ? "phase" : null;

            void Add(string? field, ServerMessage message) =>
                problems.Add(SequenceProblem.From(step.Id == Guid.Empty ? null : step.Id, field, message));

            if (step.Id == Guid.Empty)
            {
                Add("id", ServerMessages.SequenceStepWithoutId.With("number", index + 1));
            }
            else if (!ids.Add(step.Id))
            {
                Add("id", ServerMessages.SequenceStepIdRepeated.With("number", index + 1));
            }

            if (string.IsNullOrWhiteSpace(step.Name))
            {
                Add("name", ServerMessages.SequenceStepNameEmpty.With());
            }
            else if (step.Name.Length > MaxNameLength)
            {
                Add("name", ServerMessages.SequenceStepNameTooLong.With("max", MaxNameLength));
            }

            CheckConditions(step.Conditions, Add);

            if (phase == SequencePhase.WindowsPE && inWindows)
            {
                Add(phaseField, ServerMessages.SequenceWindowsPEAfterWindows.With());
            }

            if (step.RequiredPhase == SequencePhase.Windows && !imageAppliedEveryTime && !writesRawImage)
            {
                Add(phaseField, ServerMessages.SequenceWindowsNeedsImage.With());
            }

            if (step.RebootAfter && step is RebootStep)
            {
                Add("rebootAfter", ServerMessages.SequenceRestartAfterRestart.With());
            }
            else if (step.RebootAfter && phase == SequencePhase.WindowsPE && !partitioned && step is not PartitionStep)
            {
                Add("rebootAfter", noRestart.With());
            }

            // The rules of a Windows installation would only repeat what is wrong in other words.
            if (writesRawImage && (step is PartitionStep or ApplyImageStep or InjectDriversStep or WriteUnattendStep or JoinDomainStep
                || step.RequiredPhase == SequencePhase.Windows))
            {
                Add(step is RunScriptStep ? "phase" : null, ServerMessages.SequenceWindowsWithRawImage.With());
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
                        Add(null, ServerMessages.SequenceOneImage.With());
                    }

                    if (!partitioned)
                    {
                        Add(null, ServerMessages.SequenceImageBeforePartition.With());
                    }

                    CheckRunsEveryTime(step, "image", Add);
                    imageApplied = true;
                    imageAppliedEveryTime |= step.Conditions is { Count: 0 };
                    break;
                case InjectDriversStep:
                    if (!imageApplied)
                    {
                        Add(null, ServerMessages.SequenceDriversBeforeImage.With());
                    }

                    break;
                case WriteUnattendStep:
                    if (!imageApplied)
                    {
                        Add(null, ServerMessages.SequenceUnattendBeforeImage.With());
                    }

                    if (unattendWritten)
                    {
                        Add(null, ServerMessages.SequenceOneUnattend.With());
                    }

                    unattendWritten = true;
                    break;
                case JoinDomainStep:
                    if (domainJoined)
                    {
                        Add(null, ServerMessages.SequenceOneDomainJoin.With());
                    }

                    domainJoined = true;
                    break;
                case RunScriptStep script:
                    CheckScript(script, phase == SequencePhase.WindowsPE && !partitioned, writesRawImage, Add);
                    break;
                case RebootStep:
                    if (phase == SequencePhase.WindowsPE && !partitioned)
                    {
                        Add(null, noRestart.With());
                    }

                    break;
                case WriteRawImageStep:
                    if (rawImageWritten)
                    {
                        Add(null, ServerMessages.SequenceOneRawImage.With());
                    }

                    CheckRunsEveryTime(step, "rawImage", Add);
                    rawImageWritten = true;
                    break;
                case WriteCloudInitSeedStep seed:
                    if (!rawImageWritten)
                    {
                        Add(null, ServerMessages.SequenceSeedBeforeRawImage.With());
                    }

                    if (seedWritten)
                    {
                        Add(null, ServerMessages.SequenceOneSeed.With());
                    }

                    CheckSeed(seed, Add);
                    seedWritten = true;
                    break;
                default:
                    Add("kind", ServerMessages.SequenceUnknownStep.With());
                    break;
            }

            inWindows |= phase == SequencePhase.Windows;
        }

        return problems;
    }

    private static void CheckConditions(IReadOnlyList<StepCondition?>? conditions, Action<string?, ServerMessage> add)
    {
        if (conditions is null)
        {
            add("conditions", ServerMessages.SequenceConditionsMissing.With());

            return;
        }

        if (conditions.Count > MaxConditions)
        {
            add("conditions", ServerMessages.SequenceTooManyConditions.With("max", MaxConditions));
        }

        for (int index = 0; index < conditions.Count; index++)
        {
            string field = $"conditions[{index}]";

            if (conditions[index] is not { } condition)
            {
                add(field, ServerMessages.SequenceConditionEmpty.With());

                continue;
            }

            if (!MachineVariableNames.All.Contains(condition.Variable, StringComparer.Ordinal))
            {
                add($"{field}.variable", ServerMessages.SequenceConditionVariable.With("variables", string.Join(", ", MachineVariableNames.All)));
            }

            if (!Enum.IsDefined(condition.Operator))
            {
                add($"{field}.operator", ServerMessages.SequenceConditionOperator.With());
            }

            if (string.IsNullOrWhiteSpace(condition.Value))
            {
                add($"{field}.value", ServerMessages.SequenceConditionValue.With());
            }
            else if (condition.Variable == MachineVariableNames.MacAddress)
            {
                CheckMac(condition, $"{field}.value", add);
            }
        }
    }

    // Separators alone would leave nothing to compare, and StartsWith or Contains would then hold on every machine.
    private static void CheckMac(StepCondition condition, string field, Action<string?, ServerMessage> add)
    {
        string digits = ConditionEvaluator.NormaliseMac(condition.Value);
        bool hex = digits.All(char.IsAsciiHexDigit);

        if (condition.Operator is ConditionOperator.Equals or ConditionOperator.NotEquals)
        {
            if (digits.Length != 12 || !hex)
            {
                add(field, ServerMessages.MacEnterFull.With());
            }
        }
        else if (digits.Length is 0 or > 12 || !hex)
        {
            add(field, ServerMessages.MacEnterPart.With());
        }
    }

    private static void CheckPartition(PartitionStep partition, bool partitioned, bool imageApplied, Action<string?, ServerMessage> add)
    {
        if (partitioned)
        {
            add(null, ServerMessages.SequenceOnePartition.With());
        }

        if (imageApplied)
        {
            add(null, ServerMessages.SequencePartitionAfterImage.With());
        }

        CheckRunsEveryTime(partition, "partition", add);

        if (partition.SystemPartitionMegabytes is < MinSystemPartitionMegabytes or > MaxSystemPartitionMegabytes)
        {
            add(
                "systemPartitionMegabytes",
                ServerMessages.SequenceSystemPartitionSize.With("min", MinSystemPartitionMegabytes, "max", MaxSystemPartitionMegabytes));
        }

        if (partition.RecoveryPartitionMegabytes is < MinRecoveryPartitionMegabytes or > MaxRecoveryPartitionMegabytes)
        {
            add(
                "recoveryPartitionMegabytes",
                ServerMessages.SequenceRecoveryPartitionSize.With("min", MinRecoveryPartitionMegabytes, "max", MaxRecoveryPartitionMegabytes));
        }
    }

    // Later steps rely on these, so they can neither be skipped nor fail quietly. The activity is partition, image or
    // rawImage, which the messages say in words.
    private static void CheckRunsEveryTime(SequenceStep step, string activity, Action<string?, ServerMessage> add)
    {
        if (step.Conditions is { Count: > 0 })
        {
            add("conditions", ServerMessages.SequenceCannotSkip.With("activity", activity));
        }

        if (step.ContinueOnError)
        {
            add("continueOnError", ServerMessages.SequenceCannotGoOn.With("activity", activity));
        }
    }

    private static void CheckSeed(WriteCloudInitSeedStep seed, Action<string?, ServerMessage> add)
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
                add(field, ServerMessages.SequenceSeedFileMissing.With("file", file));
            }
            else if (text is not null && Encoding.UTF8.GetByteCount(text) > MaxSeedFileBytes)
            {
                add(field, ServerMessages.SequenceSeedFileTooLarge.With("file", file, "max", MaxSeedFileBytes / 1024));
            }
        }
    }

    private static void CheckScript(
        RunScriptStep script,
        bool beforePartitionInWindowsPE,
        bool writesRawImage,
        Action<string?, ServerMessage> add)
    {
        if (!Enum.IsDefined(script.Phase))
        {
            add("phase", ServerMessages.SequenceScriptPhase.With());
        }

        if (!Enum.IsDefined(script.Interpreter))
        {
            add("interpreter", ServerMessages.SequenceScriptInterpreter.With());
        }

        if (string.IsNullOrWhiteSpace(script.Script))
        {
            add("script", ServerMessages.SequenceScriptEmpty.With());
        }
        else if (Encoding.UTF8.GetByteCount(script.Script) > MaxScriptBytes)
        {
            add("script", ServerMessages.SequenceScriptTooLarge.With("max", MaxScriptBytes / 1024));
        }

        if (script.TimeoutMinutes is < 1 or > MaxTimeoutMinutes)
        {
            add("timeoutMinutes", ServerMessages.SequenceScriptTimeout.With("max", MaxTimeoutMinutes));
        }

        if (script.SuccessExitCodes is not { Count: >= 1 and <= MaxExitCodes })
        {
            add("successExitCodes", ServerMessages.SequenceSuccessExitCodes.With("max", MaxExitCodes));
        }

        // No restart codes is allowed: a script whose 3010 means success lists it among the success codes instead.
        if (script.RebootExitCodes is not { Count: <= MaxExitCodes })
        {
            add("rebootExitCodes", ServerMessages.SequenceRebootExitCodes.With("max", MaxExitCodes));
        }
        else if (script.SuccessExitCodes is not null
            && script.SuccessExitCodes.Intersect(script.RebootExitCodes).ToArray() is { Length: > 0 } both)
        {
            add("rebootExitCodes", ServerMessages.SequenceExitCodeMeansBoth.With("codes", string.Join(", ", both)));
        }

        if (beforePartitionInWindowsPE && script.PackageId is not null)
        {
            add(
                "packageId",
                writesRawImage
                    ? ServerMessages.SequencePackageWithRawImage.With()
                    : ServerMessages.SequencePackageBeforePartition.With());
        }

        if (beforePartitionInWindowsPE && script.RebootExitCodes is { Count: > 0 })
        {
            add("rebootExitCodes", (writesRawImage ? ServerMessages.SequenceRestartWithRawImage : ServerMessages.SequenceRestartBeforePartition).With());
        }
    }
}
