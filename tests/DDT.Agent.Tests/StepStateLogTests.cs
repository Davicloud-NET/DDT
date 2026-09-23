// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class StepStateLogTests
{
    private static readonly MachineVariables s_machine =
        new("Dell Inc.", "Latitude 5440", "SN-1", "4c4c4544-0042-3510-8052-b4c04f4d3232", ["00155D010203", "00155D0A0B0C"], null, SequencePhase.WindowsPE);

    private static readonly RunScriptStep s_onAnOptiPlex = Script("0193a4b2-0000-7000-8000-0000000000a1", "Only on an OptiPlex") with
    {
        Conditions =
        [
            new StepCondition(MachineVariableNames.Manufacturer, ConditionOperator.Equals, "dell inc."),
            new StepCondition(MachineVariableNames.Model, ConditionOperator.StartsWith, "OptiPlex"),
            new StepCondition(MachineVariableNames.MacAddress, ConditionOperator.Contains, "AA:BB"),
        ],
    };

    private static readonly RunScriptStep s_named = Script("0193a4b2-0000-7000-8000-0000000000a2", "Only for a named PC") with
    {
        Conditions =
        [
            new StepCondition(MachineVariableNames.ComputerName, ConditionOperator.Equals, "PC-1"),
            new StepCondition(MachineVariableNames.SerialNumber, ConditionOperator.NotEquals, "SN-2"),
        ],
    };

    private static readonly RunScriptStep s_phase = Script("0193a4b2-0000-7000-8000-0000000000a3", "Only in Windows") with
    {
        Conditions = [new StepCondition(MachineVariableNames.Phase, ConditionOperator.Equals, "Windows")],
    };

    private static readonly RunScriptStep s_tool = Script("0193a4b2-0000-7000-8000-0000000000a4", "Install a tool");

    private static readonly SequenceDefinition s_definition = new(SequenceDefinition.CurrentVersion, [s_onAnOptiPlex, s_named, s_phase, s_tool]);

    private readonly StringWriter _console = new();
    private readonly AgentLog _log;

    public StepStateLogTests()
    {
        _log = new AgentLog(new ImmediateTimeProvider(), _console);
    }

    // Only the conditions that did not hold, in the web's words, with what the machine reported for each: nothing for
    // a variable it did not report, and a MAC address with colons.
    [Fact]
    public void NamesASkippedStepWithTheConditionsThatDidNotHold()
    {
        SequenceState start = SequenceStates.Start(Guid.NewGuid(), s_definition);
        StepStateLog steps = new(_log, start, s_machine);

        steps.Saved(With(start, (0, StepState.Skipped, null)));
        steps.Saved(With(start, (0, StepState.Skipped, null), (1, StepState.Skipped, null)));
        steps.Saved(With(start, (0, StepState.Skipped, null), (1, StepState.Skipped, null), (2, StepState.Skipped, null)));

        Assert.Equal(
            [
                "INFO  Step Only on an OptiPlex was skipped, because these conditions did not hold: Model starts with \"OptiPlex\", " +
                    "and the machine reports \"Latitude 5440\"; MAC address contains \"AA:BB\", and the machine reports \"00:15:5D:01:02:03, 00:15:5D:0A:0B:0C\".",
                "INFO  Step Only for a named PC was skipped, because this condition did not hold: Computer name is \"PC-1\".",
                "INFO  Step Only in Windows was skipped, because this condition did not hold: Phase is \"Windows\", and the machine reports \"Windows PE\".",
            ],
            Lines());
    }

    // As its own error says: the Running mark saved before the step was still there when the run went on.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamesAnInterruptedStepAsTheFailureItIs(bool continueOnError)
    {
        SequenceDefinition definition = new(SequenceDefinition.CurrentVersion, [s_tool with { ContinueOnError = continueOnError }]);
        SequenceState start = With(SequenceStates.Start(Guid.NewGuid(), definition), (0, StepState.Running, null));
        StepStateLog steps = new(_log, start, s_machine);

        steps.Saved(With(start, (0, StepState.Failed, SequenceEngine.InterruptedError)) with { NextIndex = 1 });

        Assert.Equal(
            [
                "ERROR Step Install a tool failed: The machine restarted or the agent stopped while this step ran." +
                    (continueOnError ? " The run goes on, because \"Go on when this step fails\" is on for this step." : string.Empty),
            ],
            Lines());
    }

    // The run goes on from a state that already holds the skipped and the interrupted step, as after a restart.
    [Fact]
    public void SaysNothingOfStepsSettledBeforeTheRunWentOn()
    {
        SequenceState start = With(
            SequenceStates.Start(Guid.NewGuid(), s_definition),
            (0, StepState.Skipped, null),
            (1, StepState.Failed, SequenceEngine.InterruptedError),
            (2, StepState.Skipped, null));
        StepStateLog steps = new(_log, start, s_machine);

        steps.Saved(start);
        steps.Saved(With(start, (3, StepState.Running, null)));
        steps.Saved(With(start, (3, StepState.Failed, "exit code 1")));

        Assert.Empty(Lines());
    }

    // The runner's machine is in Windows PE, but a step in Windows is checked in Windows, as the engine checks it.
    [Fact]
    public void ChecksAStepInThePhaseItWouldHaveRunIn()
    {
        RunScriptStep beforeWindows = Script("0193a4b2-0000-7000-8000-0000000000a5", "Only in Windows PE") with
        {
            Phase = SequencePhase.Windows,
            Conditions = [new StepCondition(MachineVariableNames.Phase, ConditionOperator.Equals, "WindowsPE")],
        };
        SequenceState start = SequenceStates.Start(Guid.NewGuid(), new SequenceDefinition(SequenceDefinition.CurrentVersion, [s_tool, beforeWindows]));
        StepStateLog steps = new(_log, start, s_machine);

        steps.Saved(With(start, (0, StepState.Done, null), (1, StepState.Skipped, null)));

        Assert.Equal(
            ["INFO  Step Only in Windows PE was skipped, because this condition did not hold: Phase is \"WindowsPE\", and the machine reports \"Windows\"."],
            Lines());
    }

    // An interrupted step's line goes with the step's own log. A skipped step never ran, so its line is the run's. The
    // step that runs meanwhile keeps its lines.
    [Fact]
    public async Task OnlyAnInterruptedStepsLineNamesItsStep()
    {
        SequenceState start = With(SequenceStates.Start(Guid.NewGuid(), s_definition), (3, StepState.Running, null));
        StepStateLog steps = new(_log, start, s_machine);
        Guid running = Guid.NewGuid();
        _log.StepId = running;

        steps.Saved(With(start, (0, StepState.Skipped, null), (3, StepState.Failed, SequenceEngine.InterruptedError)));
        _log.Information("Still running.");

        ScriptedAgentServer server = new();
        await _log.FlushAsync(server, Guid.NewGuid(), "token", TestContext.Current.CancellationToken);
        Assert.Equal([(Guid?)null, s_tool.Id, running], server.SentLines.Select(line => line.StepId));
    }

    private static RunScriptStep Script(string id, string name) => new()
    {
        Id = Guid.Parse(id),
        Name = name,
        Phase = SequencePhase.WindowsPE,
        Script = "echo",
    };

    private static SequenceState With(SequenceState state, params (int Index, StepState State, string? Error)[] changes)
    {
        StepRunState[] steps = [.. state.Steps];

        foreach ((int index, StepState stepState, string? error) in changes)
        {
            steps[index] = steps[index] with { State = stepState, Error = error };
        }

        return state with { Steps = steps };
    }

    // Without the time.
    private string[] Lines() => [.. _console.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => line[9..])];
}
