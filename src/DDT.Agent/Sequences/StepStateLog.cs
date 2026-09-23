// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

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
public sealed class StepStateLog(AgentLog log, SequenceState start, MachineVariables machine)
{
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

    public void Saved(SequenceState state)
    {
        ArgumentNullException.ThrowIfNull(state);

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
                Write(null, $"Step {step.Name} was skipped, because {Unmet(state.Definition, index)}", failed: false);
            }
            else if (now is { State: StepState.Failed, Error: SequenceEngine.InterruptedError })
            {
                string goesOn = step.ContinueOnError ? " The run goes on, because Continue on error is on for this step." : string.Empty;
                Write(step.Id, $"Step {step.Name} failed: {SequenceEngine.InterruptedError}{goesOn}", failed: true);
            }
        }

        _last = state;
    }

    private void Write(Guid? stepId, string message, bool failed)
    {
        Guid? running = log.StepId;
        log.StepId = stepId;

        if (failed)
        {
            log.Error(message);
        }
        else
        {
            log.Information(message);
        }

        log.StepId = running;
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
