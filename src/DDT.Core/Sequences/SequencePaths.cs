// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// SequenceValidator's walk through a sequence's tree, with a PathState for all paths that reach a node. A node that
// may not run joins its input with its output. An IF joins its branches, and a failure joins the nearest container
// with ContinueOnError. A repeat's body is walked until the state an iteration starts with stops growing.
internal sealed class SequencePaths
{
    // Each pass can only add to what a repeat starts with, and the state has few bits, so this is never reached.
    private const int MaxPasses = 32;

    private readonly StepRules _rules;
    private readonly List<SequenceProblem> _problems;
    private readonly List<SequenceProblem> _warnings;
    private readonly HashSet<Guid> _ids = [];
    private readonly Dictionary<SequenceStep, PhaseSet> _phases = new(ReferenceEqualityComparer.Instance);

    // One entry per container with ContinueOnError around the node, innermost last. Each holds the join of the states
    // that failures inside it left.
    private readonly List<PathState> _catchers = [];

    // Counts nodes in the tree's pre-order. Messages that number steps use it.
    private int _number;

    // Set while a repeat's body is walked to find the state an iteration starts with.
    private bool _silent;

    public SequencePaths(SequenceDefinition definition, SequenceNames names, List<SequenceProblem> problems, List<SequenceProblem> warnings)
    {
        _problems = problems;
        _warnings = warnings;

        // A sequence either installs Windows or writes a raw disk image. The raw image step may be anywhere in it.
        _rules = new StepRules(names, SequenceTree.Nodes(definition).Any(node => node is WriteRawImageStep));
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

    // A node's own conditions are its list and its When. An IF's Test and a repeat's Until don't count.
    private static bool HasOwnConditions(SequenceStep step) => step.Conditions is not { Count: 0 } || step.When is not null;

    // Partitioning, applying the image and writing a raw disk image can't be skipped or fail quietly. StepRules refuses
    // conditions and ContinueOnError on them, so later steps are checked as if they always ran.
    private static bool RunsEveryTime(SequenceStep step) => step is PartitionStep or ApplyImageStep or WriteRawImageStep;

    private static Happened Establishes(SequenceStep step) => step switch
    {
        PartitionStep => Happened.Partitioned,
        ApplyImageStep => Happened.ImageApplied | (HasOwnConditions(step) ? Happened.None : Happened.ImageEveryTime),
        WriteUnattendStep => Happened.UnattendWritten,
        JoinDomainStep => Happened.DomainJoined,
        WriteRawImageStep => Happened.RawImageWritten,
        WriteCloudInitSeedStep => Happened.SeedWritten,
        _ => Happened.None,
    };

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
        StepProblems problems = new(step.Id == Guid.Empty ? null : step.Id, _problems, _warnings, _silent);

        if (!_silent)
        {
            CheckId(step, problems);
            _rules.CheckNode(step, depth, problems);
        }

        return step.IsContainer ? Container(step, input, depth, inRepeat, problems) : Leaf(step, input, inRepeat, problems);
    }

    private void CheckId(SequenceStep step, StepProblems problems)
    {
        _number++;

        if (step.Id == Guid.Empty)
        {
            problems.Add("id", ServerMessages.SequenceStepWithoutId.With("number", _number));
        }
        else if (!_ids.Add(step.Id))
        {
            problems.Add("id", ServerMessages.SequenceStepIdRepeated.With("number", _number));
        }
    }

    private PathState Leaf(SequenceStep step, PathState input, bool inRepeat, StepProblems problems)
    {
        // The engine changes the phase before it looks at the conditions, so a skipped step changes it too.
        SequencePhase? required = step.RequiredPhase is { } phase && Enum.IsDefined(phase) ? phase : null;
        PhaseSet runsIn = required is { } own ? Of(own) : input.Current;
        PathState entered = input.Enter(required);

        if (!_silent)
        {
            _phases[step] = _phases.GetValueOrDefault(step) | runsIn;
            _rules.CheckLeaf(step, new StepPlace(input, required, runsIn, inRepeat), problems);
        }

        PathState output = entered.With(_rules.InstallsWindows(step, required) ? Happened.None : Establishes(step));
        bool everyTime = RunsEveryTime(step);

        if (!everyTime && (HasOwnConditions(step) || step.ContinueOnError))
        {
            output = entered.Join(output);
        }

        // A failing step may have done some or none of its work. The run then ends, or continues after the nearest
        // container with ContinueOnError.
        if (everyTime || !step.ContinueOnError)
        {
            Fail(entered.Join(output));
        }

        return output;
    }

    private PathState Container(SequenceStep step, PathState input, int depth, bool inRepeat, StepProblems problems)
    {
        if (step.ContinueOnError)
        {
            _catchers.Add(PathState.Never);
        }

        PathState output = Body(step, input, depth, inRepeat, problems);

        if (step.ContinueOnError)
        {
            output = output.Join(_catchers[^1]);
            _catchers.RemoveAt(_catchers.Count - 1);
        }

        // When the container's conditions don't hold, it's skipped with everything in it.
        if (HasOwnConditions(step))
        {
            output = input.Join(output);
        }

        if (!_silent)
        {
            _rules.CheckRestartAfterContainer(step, output, problems);
            RecordPhases(step, input);
        }

        return output;
    }

    private PathState Body(SequenceStep step, PathState input, int depth, bool inRepeat, StepProblems problems)
    {
        switch (step)
        {
            case IfStep branch:
                if (!_silent)
                {
                    StepRules.CheckIf(branch, problems);
                }

                return Series(branch.Then ?? [], input, depth + 1, inRepeat).Join(Series(branch.Else ?? [], input, depth + 1, inRepeat));
            case RepeatStep repeat:
                if (!_silent)
                {
                    StepRules.CheckRepeat(repeat, problems);
                }

                PathState output = Repeat(repeat.Steps ?? [], input, depth + 1);

                // A repeat that reaches its limit without Until holding fails, unless GoOnAtLimit is set.
                if (!repeat.GoOnAtLimit)
                {
                    Fail(output);
                }

                return output;
            case GroupStep group:
                if (!_silent)
                {
                    StepRules.CheckGroup(group, problems);
                }

                return Series(group.Steps ?? [], input, depth + 1, inRepeat);
            default:
                problems.Add("kind", ServerMessages.SequenceUnknownStep.With());

                return input;
        }
    }

    // A container runs in the phase it's entered in, and in every phase its steps run in.
    private void RecordPhases(SequenceStep container, PathState input)
    {
        PhaseSet phases = input.Current;

        foreach (StepBody body in container.Bodies)
        {
            foreach (SequenceStep? inside in body.Steps)
            {
                phases |= inside is null ? PhaseSet.None : _phases.GetValueOrDefault(inside);
            }
        }

        _phases[container] = _phases.GetValueOrDefault(container) | phases;
    }

    // Each iteration starts where the last one ended, in the phase the repeat started in. A phase change inside a
    // repeat is refused separately. Otherwise it would come back as Windows PE after Windows.
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
