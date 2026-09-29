// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// The rules for each node of a sequence. SequencePaths checks them when it reaches the node.
internal sealed class StepRules(SequenceNames names, bool writesRawImage)
{
    // Before Partition, the run's state only exists in memory, so a restart in Windows PE would lose the run. A raw
    // disk image leaves no partition where DDT could keep the run's state or unpack a package.
    private readonly MessageTemplate _noRestart =
        writesRawImage ? ServerMessages.SequenceRestartWithRawImage : ServerMessages.SequenceRestartBeforePartition;

    // With a raw disk image, the Windows installation rules would only restate the same problem in other words.
    public bool InstallsWindows(SequenceStep step, SequencePhase? required) =>
        writesRawImage
        && (step is PartitionStep or ApplyImageStep or InjectDriversStep or WriteUnattendStep or JoinDomainStep
            || required == SequencePhase.Windows);

    // SequencePaths checks the id, which depends on the nodes before this one.
    public void CheckNode(SequenceStep step, int depth, StepProblems problems)
    {
        if (string.IsNullOrWhiteSpace(step.Name))
        {
            problems.Add("name", ServerMessages.SequenceStepNameEmpty.With());
        }
        else if (step.Name.Length > SequenceValidator.MaxNameLength)
        {
            problems.Add("name", ServerMessages.SequenceStepNameTooLong.With("max", SequenceValidator.MaxNameLength));
        }

        // Reported once, at the first node that's too deep, instead of at every node below it.
        if (depth == SequenceValidator.MaxDepth)
        {
            problems.Add(null, ServerMessages.SequenceTooDeep.With("max", SequenceValidator.MaxDepth));
        }

        CheckConditions(step, problems);

        // A share is connected while its step runs. A container runs nothing itself, so it can't have shares.
        if (step.IsContainer && step.Shares is { Count: > 0 })
        {
            problems.Add("shares", ServerMessages.SequenceSharesOnlyOnSteps.With());
        }
        else
        {
            names.CheckShares(step.Shares, problems.Add);
        }
    }

    public void CheckLeaf(SequenceStep step, StepPlace place, StepProblems problems)
    {
        CheckPhase(step, place, problems);
        CheckRestartAfterStep(step, place, problems);

        if (place.InRepeat && RunsOnce(step))
        {
            problems.Add(null, ServerMessages.SequenceOnceInRepeat.With());
        }

        if (InstallsWindows(step, place.Required))
        {
            problems.Add(PhaseField(step), ServerMessages.SequenceWindowsWithRawImage.With());

            return;
        }

        CheckKind(step, place, problems);
    }

    // After a container, its paths decide whether a restart is safe. Some may still be in Windows PE without a
    // partition.
    public void CheckRestartAfterContainer(SequenceStep container, PathState after, StepProblems problems)
    {
        if (container.RebootAfter && (after.Current & PhaseSet.WindowsPE) != 0 && !after.MustHave(Happened.Partitioned))
        {
            problems.Add("rebootAfter", _noRestart.With());
        }
    }

    // An IF decides by its test alone. Conditions on it would only skip both branches, and an IF around it can do that.
    public static void CheckIf(IfStep branch, StepProblems problems)
    {
        if (branch.Conditions is { Count: > 0 })
        {
            problems.Add(ConditionEvaluator.ConditionsPath, ServerMessages.SequenceIfOnlyTest.With());
        }

        if (branch.When is not null)
        {
            problems.Add(ConditionEvaluator.WhenPath, ServerMessages.SequenceIfOnlyTest.With());
        }

        if (branch.Test is null)
        {
            problems.Add(ConditionEvaluator.TestPath, ServerMessages.SequenceIfNeedsTest.With());
        }

        if (branch.Then is not { Count: > 0 } && branch.Else is not { Count: > 0 })
        {
            problems.Warn(null, ServerMessages.SequenceEmptyContainerWarning.With("kind", "if"));
        }
    }

    public static void CheckRepeat(RepeatStep repeat, StepProblems problems)
    {
        if (repeat.Until is null)
        {
            problems.Add(ConditionEvaluator.UntilPath, ServerMessages.SequenceRepeatNeedsUntil.With());
        }

        if (repeat.MaxTimes is < 1 or > SequenceValidator.MaxRepeatTimes)
        {
            problems.Add("maxTimes", ServerMessages.SequenceRepeatTimes.With("max", SequenceValidator.MaxRepeatTimes));
        }

        if (repeat.Steps is not { Count: > 0 })
        {
            problems.Warn(null, ServerMessages.SequenceEmptyContainerWarning.With("kind", "repeat"));
        }
    }

    public static void CheckGroup(GroupStep group, StepProblems problems)
    {
        if (group.Steps is not { Count: > 0 })
        {
            problems.Warn(null, ServerMessages.SequenceEmptyContainerWarning.With("kind", "group"));
        }
    }

    // Steps a run does only once, which a repeat would do again.
    private static bool RunsOnce(SequenceStep step) =>
        step is PartitionStep or WriteRawImageStep or ApplyImageStep or InjectDriversStep or WriteUnattendStep or JoinDomainStep
            or WriteCloudInitSeedStep;

    private static string? PhaseField(SequenceStep step) => step is RunScriptStep ? "phase" : null;

    private static PhaseSet Of(SequencePhase phase) => phase == SequencePhase.Windows ? PhaseSet.Windows : PhaseSet.WindowsPE;

    private void CheckConditions(SequenceStep step, StepProblems problems)
    {
        int tests = ConditionChecks.Legacy(step.Conditions, problems.Add);
        bool tooMany = false;

        void Count(ConditionNode? condition, string field)
        {
            if (condition is null)
            {
                return;
            }

            tests += ConditionChecks.Tree(condition, field, names, problems.Add);

            if (!tooMany && tests > SequenceValidator.MaxTestsPerNode)
            {
                tooMany = true;
                problems.Add(field, ServerMessages.SequenceTooManyTests.With("max", SequenceValidator.MaxTestsPerNode));
            }
        }

        Count(step.When, ConditionEvaluator.WhenPath);
        Count((step as IfStep)?.Test, ConditionEvaluator.TestPath);
        Count((step as RepeatStep)?.Until, ConditionEvaluator.UntilPath);
    }

    private void CheckPhase(SequenceStep step, StepPlace place, StepProblems problems)
    {
        PathPhases before = place.Before.Phases;
        bool afterWindows = place.Required == SequencePhase.WindowsPE
            ? (before & (PathPhases.Windows | PathPhases.WindowsPEAfterWindows)) != 0
            : place.Required is null && (before & PathPhases.WindowsPEAfterWindows) != 0;

        if (afterWindows)
        {
            problems.Add(PhaseField(step), ServerMessages.SequenceWindowsPEAfterWindows.With());
        }

        if (place.InRepeat && place.Required is { } phase && place.Before.Current != Of(phase))
        {
            problems.Add(PhaseField(step), ServerMessages.SequencePhaseChangeInRepeat.With());
        }

        if (place.Required == SequencePhase.Windows && !writesRawImage && !place.Before.MustHave(Happened.ImageEveryTime))
        {
            // When only some paths apply an image, the paths are the problem, not the step's conditions.
            MessageTemplate needsImage = !place.Before.MustHave(Happened.ImageApplied) && place.Before.MayHave(Happened.ImageApplied)
                ? ServerMessages.SequenceWindowsNeedsImageOnEveryPath
                : ServerMessages.SequenceWindowsNeedsImage;
            problems.Add(PhaseField(step), needsImage.With());
        }
    }

    private void CheckRestartAfterStep(SequenceStep step, StepPlace place, StepProblems problems)
    {
        if (step.RebootAfter && step is RebootStep)
        {
            problems.Add("rebootAfter", ServerMessages.SequenceRestartAfterRestart.With());
        }
        else if (step.RebootAfter && place.InWindowsPE && !place.Partitioned && step is not PartitionStep)
        {
            problems.Add("rebootAfter", _noRestart.With());
        }
    }

    private void CheckKind(SequenceStep step, StepPlace place, StepProblems problems)
    {
        switch (step)
        {
            case PartitionStep partition:
                SequenceValidator.CheckPartition(partition, place.Again(Happened.Partitioned), place.Again(Happened.ImageApplied), problems.Add);
                break;
            case ApplyImageStep:
                CheckImage(step, place, problems);
                break;
            case InjectDriversStep:
                if (!place.Before.MustHave(Happened.ImageApplied))
                {
                    problems.Add(null, ServerMessages.SequenceDriversBeforeImage.With());
                }

                break;
            case WriteUnattendStep unattend:
                CheckUnattend(unattend, place, problems);
                break;
            case JoinDomainStep join:
                CheckJoin(join, place, problems);
                break;
            case RunScriptStep script:
                CheckScript(script, place, problems);
                break;
            case RebootStep:
                if (place.InWindowsPE && !place.Partitioned)
                {
                    problems.Add(null, _noRestart.With());
                }

                break;
            case WriteRawImageStep:
                CheckRawImage(step, place, problems);
                break;
            case WriteCloudInitSeedStep seed:
                CheckSeed(seed, place, problems);
                break;
            case SetVariableStep set:
                names.CheckSetVariable(set, problems.Add);
                break;
            case PauseStep pause:
                CheckPause(pause, place, problems);
                break;
            default:
                problems.Add("kind", ServerMessages.SequenceUnknownStep.With());
                break;
        }
    }

    private static void CheckImage(SequenceStep step, StepPlace place, StepProblems problems)
    {
        if (place.Again(Happened.ImageApplied))
        {
            problems.Add(null, ServerMessages.SequenceOneImage.With());
        }

        if (!place.Partitioned)
        {
            problems.Add(null, ServerMessages.SequenceImageBeforePartition.With());
        }

        SequenceValidator.CheckRunsEveryTime(step, "image", problems.Add);
    }

    private void CheckUnattend(WriteUnattendStep unattend, StepPlace place, StepProblems problems)
    {
        if (!place.Before.MustHave(Happened.ImageApplied))
        {
            problems.Add(null, ServerMessages.SequenceUnattendBeforeImage.With());
        }

        if (place.Again(Happened.UnattendWritten))
        {
            problems.Add(null, ServerMessages.SequenceOneUnattend.With());
        }

        names.CheckTemplate(unattend.TimeZone, "timeZone", problems.Add);
        names.CheckTemplate(unattend.Locale, "locale", problems.Add);
        names.CheckTemplate(unattend.Keyboard, "keyboard", problems.Add);
    }

    private void CheckJoin(JoinDomainStep join, StepPlace place, StepProblems problems)
    {
        if (place.Again(Happened.DomainJoined))
        {
            problems.Add(null, ServerMessages.SequenceOneDomainJoin.With());
        }

        if (join.Account is not null)
        {
            names.CheckAccount(join.Account, "account", problems.Add);
        }

        names.CheckTemplate(join.OrganizationalUnit, "organizationalUnit", problems.Add);
    }

    private void CheckScript(RunScriptStep script, StepPlace place, StepProblems problems)
    {
        SequenceValidator.CheckScript(script, script.Phase == SequencePhase.WindowsPE && !place.Partitioned, writesRawImage, problems.Add);

        if (script.RunAs is not null)
        {
            // Windows PE has no accounts to log on with, and a script there runs as SYSTEM.
            if (script.Phase != SequencePhase.Windows)
            {
                problems.Add("runAs", ServerMessages.SequenceRunAsWindowsOnly.With());
            }

            names.CheckAccount(script.RunAs, "runAs", problems.Add);
        }
    }

    private static void CheckRawImage(SequenceStep step, StepPlace place, StepProblems problems)
    {
        if (place.Again(Happened.RawImageWritten))
        {
            problems.Add(null, ServerMessages.SequenceOneRawImage.With());
        }

        SequenceValidator.CheckRunsEveryTime(step, "rawImage", problems.Add);
    }

    private static void CheckSeed(WriteCloudInitSeedStep seed, StepPlace place, StepProblems problems)
    {
        if (!place.Before.MustHave(Happened.RawImageWritten))
        {
            problems.Add(null, ServerMessages.SequenceSeedBeforeRawImage.With());
        }

        if (place.Again(Happened.SeedWritten))
        {
            problems.Add(null, ServerMessages.SequenceOneSeed.With());
        }

        SequenceValidator.CheckSeed(seed, problems.Add);
    }

    private void CheckPause(PauseStep pause, StepPlace place, StepProblems problems)
    {
        if (pause.ContinueAfterMinutes is < 1 or > SequenceValidator.MaxPauseMinutes)
        {
            problems.Add("continueAfterMinutes", ServerMessages.SequencePauseMinutes.With("max", SequenceValidator.MaxPauseMinutes));
        }

        names.CheckTemplate(pause.Message, "message", problems.Add);

        // Someone may restart the machine while it waits, and before Partition that ends the run.
        if (place.InWindowsPE && (writesRawImage || !place.Partitioned))
        {
            problems.Warn(null, ServerMessages.SequencePauseBeforePartitionWarning.With());
        }
    }
}
