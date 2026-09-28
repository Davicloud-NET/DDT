// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// The engine keeps no log, and AgentStepRunner only sees the steps that run, so this names the steps the engine settles
// without running them, from the states the run saves: a step it skips because a condition does not hold, and a step
// it fails because it was running when the machine restarted or the agent stopped. Each saved state is compared with
// the one before it, from the state the run starts or goes on with, so a step is named once however often the run goes
// on after a restart. An interrupted step's line carries the step's id, as a running step's lines do, so the web shows
// it with the step's log. A skipped step never ran and has no log of its own on the web, so its line is the run's. The
// conditions read as the web shows them, with what the machine reported when the engine checked them.
//
// A tree's run (Format 2) also says which branch an IF took and why, each time a repeat goes through its steps, a repeat
// that stops at its limit, and a group, IF or repeat that failed and lets the run go on after it. Its reasons come from
// the tests the engine kept, since the values they read are the run's. A node on a branch an IF did not take, or inside
// a node that was skipped or failed, is passed over in silence: the line about the node that decided it says it all.
public sealed class StepStateLog(AgentLog log, SequenceState start, MachineVariables machine)
{
    private const string GoesOn = "\"Go on when this step fails\" is on for";

    private static readonly Dictionary<string, string> s_variables = new(StringComparer.Ordinal)
    {
        [MachineVariableNames.Manufacturer] = "Manufacturer",
        [MachineVariableNames.Model] = "Model",
        [MachineVariableNames.SerialNumber] = "Serial number",
        [MachineVariableNames.SmbiosUuid] = "SMBIOS UUID",
        [MachineVariableNames.MacAddress] = "MAC address",
        [MachineVariableNames.ComputerName] = "Computer name",
        [MachineVariableNames.Phase] = "Phase",
    };

    private SequenceState _last = start;
    private SequenceDefinition? _definition;
    private IReadOnlyList<SequenceStep> _nodes = [];

    public void Saved(SequenceState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Format < SequenceState.TreeFormat)
        {
            SavedList(state);
        }
        else
        {
            SavedTree(state);
        }

        _last = state;
    }

    private void SavedList(SequenceState state)
    {
        IReadOnlyList<SequenceStep> steps = state.Definition.Steps;
        int count = Math.Min(steps.Count, Math.Min(state.Steps.Count, _last.Steps.Count));

        for (int index = 0; index < count; index++)
        {
            SequenceStep step = steps[index];
            StepRunState now = state.Steps[index];

            if (now.State == _last.Steps[index].State)
            {
                continue;
            }

            if (now.State == StepState.Skipped)
            {
                Write(null, $"Step {step.Name} was skipped, because {Unmet(state.Definition, index)}", AgentLogLevel.Information);
            }
            else if (now is { State: StepState.Failed, Error: SequenceEngine.InterruptedError })
            {
                Interrupted(step);
            }
        }
    }

    // A node inside a repeat is visited again with a higher pass, so a change of pass counts as much as one of state.
    private void SavedTree(SequenceState state)
    {
        IReadOnlyList<SequenceStep> nodes = NodesOf(state.Definition);
        int count = Math.Min(nodes.Count, Math.Min(state.Steps.Count, _last.Steps.Count));

        for (int order = 0; order < count; order++)
        {
            SequenceStep node = nodes[order];
            StepRunState now = state.Steps[order];
            StepRunState before = _last.Steps[order];
            bool newVisit = now.Pass != before.Pass;

            if (now.State == before.State && !newVisit && now.Iteration == before.Iteration && now.Branch == before.Branch)
            {
                continue;
            }

            if (now.State == StepState.Skipped && now.Evaluation is { Count: > 0 } skipped && (before.State != StepState.Skipped || newVisit))
            {
                string because = ConditionStory.Sentence(
                    ConditionStory.Unmet(node, skipped),
                    "this condition did not hold",
                    "these conditions did not hold",
                    "its conditions did not hold.");
                Write(null, $"{Noun(node)} {node.Name} was skipped, because {because}", AgentLogLevel.Information);

                continue;
            }

            if (now is { State: StepState.Failed, Error: SequenceEngine.InterruptedError } && !node.IsContainer)
            {
                Interrupted(node);

                continue;
            }

            if (node.IsContainer && now.State == StepState.Failed && before.State != StepState.Failed && node.ContinueOnError)
            {
                Write(
                    node.Id,
                    $"{Noun(node)} {node.Name} failed: {now.Error} The run goes on after it, because {GoesOn} it.",
                    AgentLogLevel.Error);

                continue;
            }

            switch (node)
            {
                case IfStep choice when now.Branch is { } branch && (before.Branch != branch || newVisit):
                    Took(choice, branch, now.Evaluation ?? []);
                    break;
                case RepeatStep repeat when now.State == StepState.Running && now.Iteration > 0 && (now.Iteration != before.Iteration || newVisit):
                    Iterates(repeat, now);
                    break;
                case RepeatStep repeat when now.State == StepState.Done && before.State != StepState.Done:
                    AtLimit(repeat, now);
                    break;
            }
        }
    }

    private void Interrupted(SequenceStep step)
    {
        string goesOn = step.ContinueOnError ? $" The run goes on, because {GoesOn} this step." : string.Empty;
        Write(step.Id, $"Step {step.Name} failed: {SequenceEngine.InterruptedError}{goesOn}", AgentLogLevel.Error);
    }

    private void Took(IfStep choice, IfBranch branch, IReadOnlyList<TestEvaluation> evaluations)
    {
        (_, IReadOnlyList<string> tests) = ConditionStory.Decided(choice.Test, ConditionEvaluator.TestPath, evaluations);
        string took = $"IF {choice.Name} took {branch}";
        Write(choice.Id, tests.Count == 0 ? $"{took}." : $"{took}: {string.Join("; ", tests)}.", AgentLogLevel.Information);
    }

    // Each time through the repeat's steps, with why it goes through them again.
    private void Iterates(RepeatStep repeat, StepRunState now)
    {
        string most = MostTimes(repeat);

        if (now.Iteration == 1)
        {
            Write(repeat.Id, $"Repeat {repeat.Name} runs its steps, {most}.", AgentLogLevel.Information);

            return;
        }

        (_, IReadOnlyList<string> tests) = ConditionStory.Decided(repeat.Until, ConditionEvaluator.UntilPath, now.Evaluation ?? []);
        string time = now.Iteration.ToString(CultureInfo.InvariantCulture);
        string stop = "its condition to stop did not hold";
        Write(
            repeat.Id,
            $"Repeat {repeat.Name} runs its steps again ({time} of {most}), because {ConditionStory.Sentence(tests, stop, stop, $"{stop}.")}",
            AgentLogLevel.Information);
    }

    // A repeat done without its condition to stop holding stopped at its limit, and the run goes on after it only because
    // it may.
    private void AtLimit(RepeatStep repeat, StepRunState now)
    {
        (bool? held, IReadOnlyList<string> tests) = ConditionStory.Decided(repeat.Until, ConditionEvaluator.UntilPath, now.Evaluation ?? []);

        if (held != false || now.Iteration < Math.Max(repeat.MaxTimes, 1))
        {
            return;
        }

        string times = now.Iteration == 1 ? "once" : $"{now.Iteration.ToString(CultureInfo.InvariantCulture)} times";
        string because = tests.Count == 0 ? "" : $": {string.Join("; ", tests)}";
        Write(
            repeat.Id,
            $"Repeat {repeat.Name} ran {times}, the most it may, and its condition to stop did not hold{because}. The run goes on, " +
                "because the repeat lets it go on at its limit.",
            AgentLogLevel.Warning);
    }

    private void Write(Guid? stepId, string message, AgentLogLevel level)
    {
        Guid? running = log.StepId;
        log.StepId = stepId;

        switch (level)
        {
            case AgentLogLevel.Error:
                log.Error(message);
                break;
            case AgentLogLevel.Warning:
                log.Warning(message);
                break;
            default:
                log.Information(message);
                break;
        }

        log.StepId = running;
    }

    private IReadOnlyList<SequenceStep> NodesOf(SequenceDefinition definition)
    {
        if (!ReferenceEquals(definition, _definition))
        {
            _definition = definition;
            _nodes = SequenceTree.Nodes(definition);
        }

        return _nodes;
    }

    private static string Noun(SequenceStep node) => node switch
    {
        GroupStep => "Group",
        IfStep => "IF",
        RepeatStep => "Repeat",
        _ => "Step",
    };

    private static string MostTimes(RepeatStep repeat)
    {
        int times = Math.Max(repeat.MaxTimes, 1);

        return times == 1 ? "at most once" : $"at most {times.ToString(CultureInfo.InvariantCulture)} times";
    }

    // The conditions that did not hold, checked as the engine checked them, in the phase the step would have run in.
    private string Unmet(SequenceDefinition definition, int index)
    {
        MachineVariables inPhase = machine with { Phase = SequencePhases.Of(definition, index) };
        string[] unmet =
        [
            .. definition.Steps[index].Conditions
                .Where(condition => !ConditionEvaluator.Holds(condition, inPhase))
                .Select(condition => Describe(condition, inPhase)),
        ];

        return unmet.Length switch
        {
            0 => "its conditions did not hold.",
            1 => $"this condition did not hold: {unmet[0]}.",
            _ => $"these conditions did not hold: {string.Join("; ", unmet)}.",
        };
    }

    private static string Describe(StepCondition condition, MachineVariables machine)
    {
        string variable = s_variables.GetValueOrDefault(condition.Variable, condition.Variable);
        string operation = condition.Operator switch
        {
            ConditionOperator.Equals => "is",
            ConditionOperator.NotEquals => "is not",
            ConditionOperator.StartsWith => "starts with",
            _ => "contains",
        };
        string described = $"{variable} {operation} \"{condition.Value}\"";

        return Reported(condition.Variable, machine) is { } value ? $"{described}, and the machine reports \"{value}\"" : described;
    }

    // Null when the machine reported nothing for the variable.
    private static string? Reported(string variable, MachineVariables machine)
    {
        if (variable == MachineVariableNames.Phase)
        {
            return machine.Phase == SequencePhase.WindowsPE ? "Windows PE" : "Windows";
        }

        IReadOnlyList<string> values = machine.Values(variable);

        if (values.Count == 0)
        {
            return null;
        }

        // A MAC address as the web writes it, its bytes apart with colons.
        return variable == MachineVariableNames.MacAddress
            ? string.Join(", ", values.Select(mac => string.Join(':', mac.Chunk(2).Select(pair => new string(pair)))))
            : string.Join(", ", values);
    }
}
