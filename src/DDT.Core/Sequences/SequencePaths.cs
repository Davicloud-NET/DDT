// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// SequenceValidator's walk through a sequence's tree. Its PathState stands for every path that reaches the node it is
// at. A node that may not run, because of its own conditions or because it fails and ContinueOnError goes on, joins
// what came in with what it does; an IF joins its two branches; a failure goes on after the nearest container with
// ContinueOnError, which joins what the failure left in too; and a repeat's body is walked until the states a time
// round can start with stop growing, then once more to report. A rule that needs something done before a node asks
// whether every path did it; a rule that allows something once asks whether some path did it already.
//
// A flat sequence has one path, and the walk says of it what the validator said before sequences were trees.
internal sealed class SequencePaths
{
    // Each pass can only add to what a repeat starts with, and the state has few bits, so this is never reached.
    private const int MaxPasses = 32;

    private readonly SequenceNames _names;
    private readonly bool _writesRawImage;
    private readonly MessageTemplate _noRestart;
    private readonly List<SequenceProblem> _problems;
    private readonly List<SequenceProblem> _warnings;
    private readonly HashSet<Guid> _ids = [];
    private readonly Dictionary<SequenceStep, PhaseSet> _phases = new(ReferenceEqualityComparer.Instance);

    // For each container with ContinueOnError around the node, innermost last: the join of what failures in it left.
    private readonly List<PathState> _catchers = [];

    // Nodes in the order they are reported, which is the tree's pre-order, for the messages that count steps.
    private int _number;

    // While a repeat's body is walked to find the states a time round starts with, nothing is reported.
    private bool _silent;

    public SequencePaths(SequenceDefinition definition, SequenceNames names, List<SequenceProblem> problems, List<SequenceProblem> warnings)
    {
        _names = names;
        _problems = problems;
        _warnings = warnings;

        // A sequence either installs Windows or writes a raw disk image, wherever the step that writes it is.
        _writesRawImage = SequenceTree.Nodes(definition).Any(node => node is WriteRawImageStep);

        // Before Partition the run's state exists only in memory, so a restart in Windows PE would lose the run. A raw
        // disk image leaves no partition DDT could keep the run's state or unpack a package on.
        _noRestart = _writesRawImage ? ServerMessages.SequenceRestartWithRawImage : ServerMessages.SequenceRestartBeforePartition;
    }

    public void Walk(IReadOnlyList<SequenceStep?> steps) => Series(steps, PathState.Start, 0, inRepeat: false);

    public IReadOnlyList<NodePhase> Phases(SequenceDefinition definition) =>
    [
        .. SequenceTree.Nodes(definition).Select(node => new NodePhase(node.Id, Listed(_phases.GetValueOrDefault(node)))),
    ];

    private static List<SequencePhase> Listed(PhaseSet phases)
    {
        List<SequencePhase> listed = [];

        if ((phases & PhaseSet.WindowsPE) != 0)
        {
            listed.Add(SequencePhase.WindowsPE);
        }

        if ((phases & PhaseSet.Windows) != 0)
        {
            listed.Add(SequencePhase.Windows);
        }

        return listed;
    }

    private static PhaseSet Of(SequencePhase phase) => phase == SequencePhase.Windows ? PhaseSet.Windows : PhaseSet.WindowsPE;

    // Own conditions: a node's list and its When. An IF's Test and a repeat's Until are not its own.
    private static bool HasOwnConditions(SequenceStep step) => step.Conditions is not { Count: 0 } || step.When is not null;

    // Partitioning, applying the image and writing a raw disk image can neither be skipped nor fail quietly. Their own
    // conditions and ContinueOnError are refused on the step, so later steps are checked as if they ran, rather than
    // saying it again.
    private static bool RunsEveryTime(SequenceStep step) => step is PartitionStep or ApplyImageStep or WriteRawImageStep;

    // What a run does once, and which a repeat would do again.
    private static bool RunsOnce(SequenceStep step) =>
        step is PartitionStep or WriteRawImageStep or ApplyImageStep or InjectDriversStep or WriteUnattendStep or JoinDomainStep
            or WriteCloudInitSeedStep;

    private PathState Series(IReadOnlyList<SequenceStep?> steps, PathState state, int depth, bool inRepeat)
    {
        foreach (SequenceStep? step in steps)
        {
            if (step is not null)
            {
                state = Node(step, state, depth, inRepeat);
            }
        }

        return state;
    }

    private PathState Node(SequenceStep step, PathState input, int depth, bool inRepeat)
    {
        Guid? stepId = step.Id == Guid.Empty ? null : step.Id;

        void Add(string? field, ServerMessage message)
        {
            if (!_silent)
            {
                _problems.Add(SequenceProblem.From(stepId, field, message));
            }
        }

        void Warn(string? field, ServerMessage message)
        {
            if (!_silent)
            {
                _warnings.Add(SequenceProblem.From(stepId, field, message));
            }
        }

        if (!_silent)
        {
            CheckNode(step, depth, Add);
        }

        return step.IsContainer ? Container(step, input, depth, inRepeat, Add, Warn) : Leaf(step, input, inRepeat, Add, Warn);
    }

    // What every node has: an id, a name, its conditions and its shares.
    private void CheckNode(SequenceStep step, int depth, Action<string?, ServerMessage> add)
    {
        _number++;

        if (step.Id == Guid.Empty)
        {
            add("id", ServerMessages.SequenceStepWithoutId.With("number", _number));
        }
        else if (!_ids.Add(step.Id))
        {
            add("id", ServerMessages.SequenceStepIdRepeated.With("number", _number));
        }

        if (string.IsNullOrWhiteSpace(step.Name))
        {
            add("name", ServerMessages.SequenceStepNameEmpty.With());
        }
        else if (step.Name.Length > SequenceValidator.MaxNameLength)
        {
            add("name", ServerMessages.SequenceStepNameTooLong.With("max", SequenceValidator.MaxNameLength));
        }

        // Said once, at the first node too deep, rather than at every node below it.
        if (depth == SequenceValidator.MaxDepth)
        {
            add(null, ServerMessages.SequenceTooDeep.With("max", SequenceValidator.MaxDepth));
        }

        int tests = ConditionChecks.Legacy(step.Conditions, add);
        bool tooMany = false;

        void Count(ConditionNode? condition, string field)
        {
            if (condition is null)
            {
                return;
            }

            tests += ConditionChecks.Tree(condition, field, _names, add);

            if (!tooMany && tests > SequenceValidator.MaxTestsPerNode)
            {
                tooMany = true;
                add(field, ServerMessages.SequenceTooManyTests.With("max", SequenceValidator.MaxTestsPerNode));
            }
        }

        Count(step.When, ConditionEvaluator.WhenPath);
        Count((step as IfStep)?.Test, ConditionEvaluator.TestPath);
        Count((step as RepeatStep)?.Until, ConditionEvaluator.UntilPath);

        // A share is connected for as long as its step runs, so a container, which runs nothing itself, has none.
        if (step.IsContainer && step.Shares is { Count: > 0 })
        {
            add("shares", ServerMessages.SequenceSharesOnlyOnSteps.With());
        }
        else
        {
            _names.CheckShares(step.Shares, add);
        }
    }

    private PathState Leaf(
        SequenceStep step,
        PathState input,
        bool inRepeat,
        Action<string?, ServerMessage> add,
        Action<string?, ServerMessage> warn)
    {
        // The engine changes the phase before it looks at the conditions, so a skipped step changes it too.
        SequencePhase? required = step.RequiredPhase is { } phase && Enum.IsDefined(phase) ? phase : null;
        PhaseSet runsIn = required is { } own ? Of(own) : input.Current;
        PathState entered = input.Enter(required);

        // The rules of a Windows installation would only repeat what is wrong in other words.
        bool installsWindows = _writesRawImage
            && (step is PartitionStep or ApplyImageStep or InjectDriversStep or WriteUnattendStep or JoinDomainStep
                || required == SequencePhase.Windows);

        if (!_silent)
        {
            _phases[step] = _phases.GetValueOrDefault(step) | runsIn;
            CheckLeaf(step, input, required, runsIn, inRepeat, installsWindows, add, warn);
        }

        Happened done = installsWindows ? Happened.None : step switch
        {
            PartitionStep => Happened.Partitioned,
            ApplyImageStep => Happened.ImageApplied | (HasOwnConditions(step) ? Happened.None : Happened.ImageEveryTime),
            WriteUnattendStep => Happened.UnattendWritten,
            JoinDomainStep => Happened.DomainJoined,
            WriteRawImageStep => Happened.RawImageWritten,
            WriteCloudInitSeedStep => Happened.SeedWritten,
            _ => Happened.None,
        };
        PathState output = entered.With(done);
        bool everyTime = RunsEveryTime(step);

        if (!everyTime && (HasOwnConditions(step) || step.ContinueOnError))
        {
            output = entered.Join(output);
        }

        // A step that fails, having done some or none of its work, ends the run or goes on after the nearest container
        // with ContinueOnError.
        if (everyTime || !step.ContinueOnError)
        {
            Fail(entered.Join(output));
        }

        return output;
    }

    private void CheckLeaf(
        SequenceStep step,
        PathState input,
        SequencePhase? required,
        PhaseSet runsIn,
        bool inRepeat,
        bool installsWindows,
        Action<string?, ServerMessage> add,
        Action<string?, ServerMessage> warn)
    {
        string? phaseField = step is RunScriptStep ? "phase" : null;
        bool inWindowsPE = (runsIn & PhaseSet.WindowsPE) != 0;
        bool partitioned = input.MustHave(Happened.Partitioned);

        // Inside a repeat, the kinds a run does once are refused as such; saying again that they are done already
        // would only be what the next time round does.
        bool once = !inRepeat;

        bool afterWindows = required == SequencePhase.WindowsPE
            ? (input.Phases & (PathPhases.Windows | PathPhases.WindowsPEAfterWindows)) != 0
            : required is null && (input.Phases & PathPhases.WindowsPEAfterWindows) != 0;

        if (afterWindows)
        {
            add(phaseField, ServerMessages.SequenceWindowsPEAfterWindows.With());
        }

        if (inRepeat && required is { } phase && input.Current != Of(phase))
        {
            add(phaseField, ServerMessages.SequencePhaseChangeInRepeat.With());
        }

        if (required == SequencePhase.Windows && !_writesRawImage && !input.MustHave(Happened.ImageEveryTime))
        {
            // An image on some paths but not on every one is a matter of the paths, not of the step's conditions.
            MessageTemplate needsImage = !input.MustHave(Happened.ImageApplied) && input.MayHave(Happened.ImageApplied)
                ? ServerMessages.SequenceWindowsNeedsImageOnEveryPath
                : ServerMessages.SequenceWindowsNeedsImage;
            add(phaseField, needsImage.With());
        }

        if (step.RebootAfter && step is RebootStep)
        {
            add("rebootAfter", ServerMessages.SequenceRestartAfterRestart.With());
        }
        else if (step.RebootAfter && inWindowsPE && !partitioned && step is not PartitionStep)
        {
            add("rebootAfter", _noRestart.With());
        }

        if (inRepeat && RunsOnce(step))
        {
            add(null, ServerMessages.SequenceOnceInRepeat.With());
        }

        if (installsWindows)
        {
            add(phaseField, ServerMessages.SequenceWindowsWithRawImage.With());

            return;
        }

        switch (step)
        {
            case PartitionStep partition:
                SequenceValidator.CheckPartition(
                    partition,
                    once && input.MayHave(Happened.Partitioned),
                    once && input.MayHave(Happened.ImageApplied),
                    add);
                break;
            case ApplyImageStep:
                if (once && input.MayHave(Happened.ImageApplied))
                {
                    add(null, ServerMessages.SequenceOneImage.With());
                }

                if (!partitioned)
                {
                    add(null, ServerMessages.SequenceImageBeforePartition.With());
                }

                SequenceValidator.CheckRunsEveryTime(step, "image", add);
                break;
            case InjectDriversStep:
                if (!input.MustHave(Happened.ImageApplied))
                {
                    add(null, ServerMessages.SequenceDriversBeforeImage.With());
                }

                break;
            case WriteUnattendStep unattend:
                if (!input.MustHave(Happened.ImageApplied))
                {
                    add(null, ServerMessages.SequenceUnattendBeforeImage.With());
                }

                if (once && input.MayHave(Happened.UnattendWritten))
                {
                    add(null, ServerMessages.SequenceOneUnattend.With());
                }

                _names.CheckTemplate(unattend.TimeZone, "timeZone", add);
                _names.CheckTemplate(unattend.Locale, "locale", add);
                _names.CheckTemplate(unattend.Keyboard, "keyboard", add);
                break;
            case JoinDomainStep join:
                if (once && input.MayHave(Happened.DomainJoined))
                {
                    add(null, ServerMessages.SequenceOneDomainJoin.With());
                }

                if (join.Account is not null)
                {
                    _names.CheckAccount(join.Account, "account", add);
                }

                _names.CheckTemplate(join.OrganizationalUnit, "organizationalUnit", add);
                break;
            case RunScriptStep script:
                SequenceValidator.CheckScript(script, script.Phase == SequencePhase.WindowsPE && !partitioned, _writesRawImage, add);

                if (script.RunAs is not null)
                {
                    // Windows PE has no accounts to log on with, and a script there runs as SYSTEM.
                    if (script.Phase != SequencePhase.Windows)
                    {
                        add("runAs", ServerMessages.SequenceRunAsWindowsOnly.With());
                    }

                    _names.CheckAccount(script.RunAs, "runAs", add);
                }

                break;
            case RebootStep:
                if (inWindowsPE && !partitioned)
                {
                    add(null, _noRestart.With());
                }

                break;
            case WriteRawImageStep:
                if (once && input.MayHave(Happened.RawImageWritten))
                {
                    add(null, ServerMessages.SequenceOneRawImage.With());
                }

                SequenceValidator.CheckRunsEveryTime(step, "rawImage", add);
                break;
            case WriteCloudInitSeedStep seed:
                if (!input.MustHave(Happened.RawImageWritten))
                {
                    add(null, ServerMessages.SequenceSeedBeforeRawImage.With());
                }

                if (once && input.MayHave(Happened.SeedWritten))
                {
                    add(null, ServerMessages.SequenceOneSeed.With());
                }

                SequenceValidator.CheckSeed(seed, add);
                break;
            case SetVariableStep set:
                _names.CheckSetVariable(set, add);
                break;
            case PauseStep pause:
                if (pause.ContinueAfterMinutes is < 1 or > SequenceValidator.MaxPauseMinutes)
                {
                    add("continueAfterMinutes", ServerMessages.SequencePauseMinutes.With("max", SequenceValidator.MaxPauseMinutes));
                }

                _names.CheckTemplate(pause.Message, "message", add);

                // Someone may restart the machine while it waits, and before Partition that ends the run.
                if (inWindowsPE && (_writesRawImage || !partitioned))
                {
                    warn(null, ServerMessages.SequencePauseBeforePartitionWarning.With());
                }

                break;
            default:
                add("kind", ServerMessages.SequenceUnknownStep.With());
                break;
        }
    }

    private PathState Container(
        SequenceStep step,
        PathState input,
        int depth,
        bool inRepeat,
        Action<string?, ServerMessage> add,
        Action<string?, ServerMessage> warn)
    {
        if (step.ContinueOnError)
        {
            _catchers.Add(PathState.Never);
        }

        PathState output;

        switch (step)
        {
            case IfStep branch:
                if (!_silent)
                {
                    CheckIf(branch, add, warn);
                }

                output = Series(branch.Then ?? [], input, depth + 1, inRepeat).Join(Series(branch.Else ?? [], input, depth + 1, inRepeat));
                break;
            case RepeatStep repeat:
                if (!_silent)
                {
                    CheckRepeat(repeat, add, warn);
                }

                output = Repeat(repeat.Steps ?? [], input, depth + 1);

                // At its limit, a repeat whose Until never held fails, unless it goes on.
                if (!repeat.GoOnAtLimit)
                {
                    Fail(output);
                }

                break;
            case GroupStep group:
                if (!_silent && group.Steps is not { Count: > 0 })
                {
                    warn(null, ServerMessages.SequenceEmptyContainerWarning.With("kind", "group"));
                }

                output = Series(group.Steps ?? [], input, depth + 1, inRepeat);
                break;
            default:
                add("kind", ServerMessages.SequenceUnknownStep.With());
                output = input;
                break;
        }

        if (step.ContinueOnError)
        {
            output = output.Join(_catchers[^1]);
            _catchers.RemoveAt(_catchers.Count - 1);
        }

        // When its own conditions do not hold, the container is skipped with everything in it.
        if (HasOwnConditions(step))
        {
            output = input.Join(output);
        }

        if (!_silent)
        {
            if (step.RebootAfter && (output.Current & PhaseSet.WindowsPE) != 0 && !output.MustHave(Happened.Partitioned))
            {
                add("rebootAfter", _noRestart.With());
            }

            PhaseSet phases = input.Current;

            foreach (StepBody body in step.Bodies)
            {
                foreach (SequenceStep? inside in body.Steps)
                {
                    phases |= inside is null ? PhaseSet.None : _phases.GetValueOrDefault(inside);
                }
            }

            _phases[step] = _phases.GetValueOrDefault(step) | phases;
        }

        return output;
    }

    // An IF decides by its test alone: its own conditions would only skip both branches, which an IF around it says.
    private static void CheckIf(IfStep branch, Action<string?, ServerMessage> add, Action<string?, ServerMessage> warn)
    {
        if (branch.Conditions is { Count: > 0 })
        {
            add(ConditionEvaluator.ConditionsPath, ServerMessages.SequenceIfOnlyTest.With());
        }

        if (branch.When is not null)
        {
            add(ConditionEvaluator.WhenPath, ServerMessages.SequenceIfOnlyTest.With());
        }

        if (branch.Test is null)
        {
            add(ConditionEvaluator.TestPath, ServerMessages.SequenceIfNeedsTest.With());
        }

        if (branch.Then is not { Count: > 0 } && branch.Else is not { Count: > 0 })
        {
            warn(null, ServerMessages.SequenceEmptyContainerWarning.With("kind", "if"));
        }
    }

    private static void CheckRepeat(RepeatStep repeat, Action<string?, ServerMessage> add, Action<string?, ServerMessage> warn)
    {
        if (repeat.Until is null)
        {
            add(ConditionEvaluator.UntilPath, ServerMessages.SequenceRepeatNeedsUntil.With());
        }

        if (repeat.MaxTimes is < 1 or > SequenceValidator.MaxRepeatTimes)
        {
            add("maxTimes", ServerMessages.SequenceRepeatTimes.With("max", SequenceValidator.MaxRepeatTimes));
        }

        if (repeat.Steps is not { Count: > 0 })
        {
            warn(null, ServerMessages.SequenceEmptyContainerWarning.With("kind", "repeat"));
        }
    }

    // A time round starts where the last one ended, in the phase the repeat started in: a phase change inside is
    // refused on its own, and would otherwise come back as Windows PE after Windows.
    private PathState Repeat(IReadOnlyList<SequenceStep?> body, PathState input, int depth)
    {
        bool silent = _silent;
        PathState start = input;
        _silent = true;

        for (int pass = 1; pass < MaxPasses; pass++)
        {
            PathState end = Series(body, start, depth, inRepeat: true);
            PathState next = start.Join(end with { Phases = input.Phases });

            if (next == start)
            {
                break;
            }

            start = next;
        }

        _silent = silent;

        return Series(body, start, depth, inRepeat: true);
    }

    private void Fail(PathState state)
    {
        if (_catchers.Count > 0)
        {
            _catchers[^1] = _catchers[^1].Join(state);
        }
    }
}
