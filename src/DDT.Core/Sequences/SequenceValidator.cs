// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// Checks that a sequence can run, on every path through its tree. The server keeps a sequence with problems as a draft
// and never runs it, and the agent checks again. The document comes from outside, so members declared non-null can
// still be null.
public static class SequenceValidator
{
    // Steps are the leaves of the tree. Nodes are the leaves plus the groups, IFs and repeats that hold them.
    public const int MaxSteps = 100;
    public const int MaxNodes = 200;

    // Levels of nodes, the top one included.
    public const int MaxDepth = 8;
    public const int MaxNameLength = 100;
    public const int MaxConditions = 10;

    // How deep groups can nest in a condition, and how many tests all of a node's conditions have together.
    public const int MaxConditionDepth = 4;
    public const int MaxTestsPerNode = 20;
    public const int MaxRepeatTimes = 100;
    public const int MaxPauseMinutes = 24 * 60;
    public const int MaxShares = 4;
    public const int MaxVariables = 64;
    public const int MaxInputs = 16;
    public const int MaxChoices = 50;
    public const int MaxAnswerLength = 1024;
    public const int MaxValueNameLength = 64;
    public const int MaxScriptBytes = 64 * 1024;
    public const int MaxTimeoutMinutes = 24 * 60;
    public const int MaxExitCodes = 16;
    public const int MaxSeedFileBytes = 64 * 1024;

    // Microsoft's minimums are 260 MB for the system partition on 4K native disks and 300 MB for a recovery
    // partition. The upper limits only catch typos, which would otherwise fail after the disk was cleaned.
    private const int MinSystemPartitionMegabytes = 260;
    private const int MaxSystemPartitionMegabytes = 4096;
    private const int MinRecoveryPartitionMegabytes = 300;
    private const int MaxRecoveryPartitionMegabytes = 65536;

    // Values DDT gives every run besides the machine's facts. Templates may use them without the sequence declaring
    // them. They're the deployment defaults that the answer file and the domain join use.
    public static IReadOnlyList<string> WellKnownNames { get; } = ["TimeZone", "Locale", "Keyboard", "OrganizationalUnit"];

    public static IReadOnlyList<SequenceProblem> Validate(SequenceDefinition definition) => Analyse(definition).Problems;

    public static SequenceAnalysis Analyse(SequenceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        List<SequenceProblem> problems = [];
        List<SequenceProblem> warnings = [];

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
        int leaves = 0;
        int nodes = 0;
        Count(steps, ref leaves, ref nodes);

        if (leaves is 0 or > MaxSteps)
        {
            problems.Add(SequenceProblem.From(null, "steps", ServerMessages.SequenceStepCount.With("max", MaxSteps)));
        }

        if (nodes > MaxNodes)
        {
            problems.Add(SequenceProblem.From(null, "steps", ServerMessages.SequenceNodeCount.With("max", MaxNodes)));
        }

        // A tree with a hole in it reports little more than the hole. Its phases are still worked out, so a page can
        // show them.
        List<SequenceProblem> empty = [];
        Empty(steps, null, "steps", empty);
        problems.AddRange(empty);

        SequenceNames names = new(definition);
        bool whole = empty.Count == 0;

        if (whole)
        {
            names.CheckDeclarations(problems);
        }

        SequencePaths paths = new(definition, names, whole ? problems : [], whole ? warnings : []);
        paths.Walk(steps);

        return new SequenceAnalysis([.. problems.Distinct()], [.. warnings.Distinct()], paths.Phases(definition), names.ValueNames);
    }

    // An empty slot counts as a step, like in a flat sequence.
    private static void Count(IReadOnlyList<SequenceStep?> steps, ref int leaves, ref int nodes)
    {
        foreach (SequenceStep? step in steps)
        {
            nodes++;

            if (step is not { IsContainer: true })
            {
                leaves++;

                continue;
            }

            foreach (StepBody body in step.Bodies)
            {
                Count(body.Steps, ref leaves, ref nodes);
            }
        }
    }

    // Reports a list with an empty slot. The problem goes on the container that holds the list, or on the sequence for
    // the top level.
    private static void Empty(IReadOnlyList<SequenceStep?> steps, Guid? containerId, string field, List<SequenceProblem> empty)
    {
        if (steps.Contains(null))
        {
            empty.Add(SequenceProblem.From(containerId, field, ServerMessages.SequenceStepEmpty.With()));
        }

        foreach (SequenceStep? step in steps)
        {
            foreach (StepBody body in step?.Bodies ?? [])
            {
                Empty(body.Steps, step!.Id == Guid.Empty ? null : step.Id, body.Name, empty);
            }
        }
    }

    internal static void CheckPartition(PartitionStep partition, bool again, bool afterImage, Action<string?, ServerMessage> add)
    {
        if (again)
        {
            add(null, ServerMessages.SequenceOnePartition.With());
        }

        if (afterImage)
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

    // Later steps rely on these steps, so conditions and ContinueOnError on them are refused. Inside an IF or a group
    // with conditions, the paths decide instead. activity is partition, image or rawImage, for the messages.
    internal static void CheckRunsEveryTime(SequenceStep step, string activity, Action<string?, ServerMessage> add)
    {
        if (step.Conditions is { Count: > 0 })
        {
            add(ConditionEvaluator.ConditionsPath, ServerMessages.SequenceCannotSkip.With("activity", activity));
        }

        if (step.When is not null)
        {
            add(ConditionEvaluator.WhenPath, ServerMessages.SequenceCannotSkip.With("activity", activity));
        }

        if (step.ContinueOnError)
        {
            add("continueOnError", ServerMessages.SequenceCannotGoOn.With("activity", activity));
        }
    }

    internal static void CheckSeed(WriteCloudInitSeedStep seed, Action<string?, ServerMessage> add)
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

    internal static void CheckScript(
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

        // An empty list of restart codes is allowed. A script whose 3010 means success lists it among the success codes
        // instead.
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
