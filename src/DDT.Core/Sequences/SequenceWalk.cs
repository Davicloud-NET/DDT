// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// The run as the engine walks it, in the shape of Format 2 whatever its format: one state per node in pre-order, the
// cursor, the phase and the run variables. Snapshot gives it in the run's own format.
internal sealed class SequenceWalk
{
    private readonly SequenceState _given;
    private readonly bool _flat;
    private readonly IReadOnlyList<SequenceStep> _nodes;
    private readonly IReadOnlyDictionary<Guid, NodePosition> _index;
    private readonly int[] _sizes;
    private readonly StepRunState[] _steps;

    public SequenceWalk(SequenceState state)
    {
        _flat = state.Format < SequenceState.TreeFormat;
        _nodes = SequenceTree.Nodes(state.Definition);
        _index = SequenceTree.Index(state.Definition);

        // A state of Format 1 holds a flat list, one step after the other. One of Format 2 names each node, since
        // its cursor finds them by id.
        bool fits = _flat
            ? state.NextIndex >= 0 && _nodes.Count == (state.Definition.Steps?.Count ?? 0) && !_nodes.Any(node => node.IsContainer)
            : state.Steps is not null && state.Steps.Select(step => step.StepId).SequenceEqual(_nodes.Select(node => node.Id));

        if (state.Steps is null || state.Steps.Count != _nodes.Count || !fits)
        {
            throw new ArgumentException("The run's state does not have one entry per step of its definition.", nameof(state));
        }

        SequenceState tree = SequenceStates.Upgrade(state);

        if (tree.Cursor is { } cursor && !_index.ContainsKey(cursor.NodeId))
        {
            throw new ArgumentException("The run's state goes on at a step its definition does not have.", nameof(state));
        }

        _given = state;
        _sizes = [.. _nodes.Select(Size)];
        _steps = [.. tree.Steps];
        Cursor = tree.Cursor;
        Variables = tree.Variables;
        Saved = state;
    }

    public NodeCursor? Cursor { get; private set; }

    // The engine never changes the phase: the host does, when it hands the run over.
    public SequencePhase Phase => _given.Phase;

    public IReadOnlyDictionary<string, string> Variables { get; private set; }

    // The state last saved, or the one the engine started with.
    public SequenceState Saved { get; private set; }

    // Whether anything changed since the last save.
    public bool Changed { get; private set; }

    public SequenceDefinition Definition => _given.Definition;

    public Guid RunId => _given.RunId;

    public StepRunState this[int order] => _steps[order];

    public SequenceStep Node(Guid id) => _index[id].Step;

    public int OrderOf(SequenceStep node) => _index[node.Id].Order;

    // What conditions and templates read in the phase: in a tree's run the run's values with the variables steps set on
    // top, LastStepFailed and LastExitCode among them; in a flat run the machine alone.
    public MachineVariables Machine(MachineVariables machine, SequencePhase phase)
    {
        if (_flat)
        {
            return machine with { Phase = phase };
        }

        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string name, string value) in machine.Variables ?? new Dictionary<string, string>())
        {
            values[name] = value;
        }

        foreach ((string name, string value) in Variables)
        {
            values[name] = value;
        }

        return machine with { Phase = phase, Variables = values };
    }

    // Format 1 keeps NextIndex and none of the members of a tree, so an older agent resumes it.
    public SequenceState Snapshot() => _flat
        ? _given with
        {
            NextIndex = Cursor is { } cursor ? _index[cursor.NodeId].Order : _nodes.Count,
            Steps = [.. _steps.Select(step => new StepRunState(step.StepId, step.State, step.Error))],
            Variables = Variables,
            Cursor = null,
        }
        : _given with { NextIndex = SequenceStates.NoNextIndex, Steps = [.. _steps], Variables = Variables, Cursor = Cursor };

    public void WasSaved(SequenceState state)
    {
        Saved = state;
        Changed = false;
    }

    public SequenceRunResult Result(SequenceOutcome outcome) => new(outcome, Snapshot(), null);

    public SequenceRunResult Failed(string error) => new(SequenceOutcome.Failed, Snapshot(), error);

    // The leaf's conditions do not hold: it is skipped, with the tests that decided it.
    public void Skip(int order, IReadOnlyList<TestEvaluation> evaluations)
    {
        _steps[order] = Visit(_steps[order], StepState.Skipped, evaluations);
        Cursor = Successor(order);
        Changed = true;
    }

    public void Begin(int order, IReadOnlyList<TestEvaluation> evaluations)
    {
        _steps[order] = Visit(_steps[order], StepState.Running, evaluations);
        Changed = true;
    }

    public void Mark(int order, StepState state, string? error)
    {
        _steps[order] = _steps[order] with { State = state, Error = error };
        Changed = true;
    }

    // A leaf ran: its outputs join the run variables, and in a tree's run LastStepFailed and LastExitCode say how it
    // went, for the conditions after it.
    public void Record(StepResult result)
    {
        Dictionary<string, string>? variables = null;

        foreach ((string name, string value) in result.Outputs ?? new Dictionary<string, string>())
        {
            variables ??= new Dictionary<string, string>(Variables, StringComparer.Ordinal);
            variables[name] = value;
        }

        if (!_flat)
        {
            variables ??= new Dictionary<string, string>(Variables, StringComparer.Ordinal);
            variables[MachineVariableNames.LastStepFailed] = result.Outcome == StepOutcome.Failed ? SequenceEngine.StepFailed : SequenceEngine.StepSucceeded;

            if (result.ExitCode is { } exitCode)
            {
                variables[MachineVariableNames.LastExitCode] = exitCode.ToString(CultureInfo.InvariantCulture);
            }
        }

        if (variables is not null)
        {
            Variables = variables;
            Changed = true;
        }
    }

    public void Finish(int order)
    {
        _steps[order] = _steps[order] with { State = StepState.Done, Error = null };
        Cursor = Successor(order);
        Changed = true;
    }

    // Fails the node and its containers up to the nearest with ContinueOnError, the node itself first, and goes on
    // after it, skipping what did not run. Returns false when none caught the failure, which fails the run.
    public bool Fail(int order, string error)
    {
        NodePosition position = _index[_nodes[order].Id];
        Changed = true;

        while (true)
        {
            _steps[position.Order] = _steps[position.Order] with { State = StepState.Failed, Error = error };

            if (position.Step.ContinueOnError)
            {
                for (int inside = position.Order + 1; inside < position.Order + _sizes[position.Order]; inside++)
                {
                    if (_steps[inside].State == StepState.Pending)
                    {
                        _steps[inside] = Visit(_steps[inside], StepState.Skipped, null);
                    }
                }

                Cursor = Successor(position.Order);

                return true;
            }

            if (position.ParentId is not { } parentId)
            {
                Cursor = Successor(position.Order);

                return false;
            }

            position = _index[parentId];
        }
    }

    // A container whose own conditions do not hold is skipped with everything in it. An IF keeps the branch it took and
    // skips the other, which the server needs to see the run complete.
    public void Enter(SequenceStep container, MachineVariables machine)
    {
        int order = OrderOf(container);
        ConditionResult own = ConditionEvaluator.Evaluate(container, machine);
        IReadOnlyList<StepBody> bodies = container.Bodies;
        Changed = true;

        if (!own.Held)
        {
            _steps[order] = Visit(_steps[order], StepState.Skipped, own.Evaluations);
            SkipRange(order + 1, _sizes[order] - 1);
            Cursor = Successor(order);

            return;
        }

        switch (container)
        {
            case IfStep choice:
                ConditionResult test = ConditionEvaluator.Evaluate(choice.Test, machine, ConditionEvaluator.TestPath);
                int taken = test.Held ? 0 : 1;
                int other = 1 - taken;
                _steps[order] = Visit(_steps[order], StepState.Running, [.. own.Evaluations, .. test.Evaluations]) with
                {
                    Branch = test.Held ? IfBranch.Then : IfBranch.Else,
                };
                SkipRange(BodyStart(order, other), BodySize(bodies[other]));
                Cursor = First(container, bodies[taken].Steps);

                break;

            case RepeatStep:
                _steps[order] = Visit(_steps[order], StepState.Running, own.Evaluations) with { Iteration = 1 };
                ResetBody(order);
                Cursor = First(container, [.. bodies.SelectMany(body => body.Steps)]);

                break;

            default:
                _steps[order] = Visit(_steps[order], StepState.Running, own.Evaluations);
                Cursor = First(container, [.. bodies.SelectMany(body => body.Steps)]);

                break;
        }
    }

    // Leaving a container once its steps are done. A repeat tests Until after each time through them, do ... until,
    // and goes through them again while it may. Returns the error when the run fails with it.
    public string? Leave(SequenceStep container, MachineVariables machine)
    {
        int order = OrderOf(container);
        Changed = true;

        if (container is RepeatStep repeat)
        {
            ConditionResult until = ConditionEvaluator.Evaluate(repeat.Until, machine, ConditionEvaluator.UntilPath);
            StepRunState run = _steps[order];
            run = run with
            {
                Evaluation = Bounded([.. (run.Evaluation ?? []).Where(test => !IsUntil(test)), .. until.Evaluations]),
            };
            _steps[order] = run;

            // A document from outside may leave MaxTimes out, which reads as 0; the validator refuses it.
            int times = Math.Max(repeat.MaxTimes, 1);

            if (!until.Held && run.Iteration < times)
            {
                _steps[order] = run with { Iteration = run.Iteration + 1 };
                ResetBody(order);
                Cursor = First(container, [.. container.Bodies.SelectMany(body => body.Steps)]);

                return null;
            }

            if (!until.Held && !repeat.GoOnAtLimit)
            {
                string error = $"The repeat {repeat.Name} ran {(times == 1 ? "once" : $"{times} times")}, the most it may, " +
                    "and its condition to stop never held.";

                return Fail(order, error) ? null : error;
            }
        }

        _steps[order] = _steps[order] with { State = StepState.Done, Error = null };
        Cursor = Successor(order);

        return null;
    }

    // A new visit of a node: its pass goes up, and nothing an earlier visit left stays.
    private static StepRunState Visit(StepRunState step, StepState state, IReadOnlyList<TestEvaluation>? evaluations) =>
        step with
        {
            State = state,
            Error = null,
            Pass = step.Pass + 1,
            Iteration = 0,
            Branch = null,
            Evaluation = Bounded(evaluations ?? []),
        };

    private static IReadOnlyList<TestEvaluation>? Bounded(IReadOnlyList<TestEvaluation> evaluations) =>
        evaluations.Count == 0 ? null : [.. evaluations.Take(TestEvaluation.MaxPerNode)];

    private static bool IsUntil(TestEvaluation test) =>
        test.Path.StartsWith(ConditionEvaluator.UntilPath, StringComparison.Ordinal);

    // The node and every node inside it, as the pre-order walk counts them.
    private static int Size(SequenceStep node) => 1 + node.Bodies.Sum(BodySize);

    private static int BodySize(StepBody body) =>
        ((IReadOnlyList<SequenceStep?>)body.Steps).Sum(node => node is null ? 0 : Size(node));

    // Where the run goes into a container: its first node, or, with nothing in it, straight out again.
    private static NodeCursor First(SequenceStep container, IReadOnlyList<SequenceStep?> nodes) =>
        nodes.FirstOrDefault(node => node is not null) is { } first
            ? new NodeCursor(first.Id, false)
            : new NodeCursor(container.Id, true);

    private NodeCursor? Successor(int order) => SequenceTree.Successor(Definition, _index, _nodes[order].Id);

    // The order of a body's first node: the nodes of the bodies before it come first.
    private int BodyStart(int order, int body) => order + 1 + _nodes[order].Bodies.Take(body).Sum(BodySize);

    // Skipping counts as a visit, so the pass of each node goes up as if it had been entered.
    private void SkipRange(int start, int count)
    {
        for (int inside = start; inside < start + count; inside++)
        {
            _steps[inside] = Visit(_steps[inside], StepState.Skipped, null);
        }
    }

    // Every node inside a repeat is Pending again for its next time through, and keeps its pass.
    private void ResetBody(int order)
    {
        for (int inside = order + 1; inside < order + _sizes[order]; inside++)
        {
            _steps[inside] = _steps[inside] with
            {
                State = StepState.Pending,
                Error = null,
                Iteration = 0,
                Branch = null,
                Evaluation = null,
            };
        }
    }
}
