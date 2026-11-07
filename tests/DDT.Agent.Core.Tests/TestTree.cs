// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// A tree with a little of everything, for the engine to walk without the agent's steps: an IF on the model, a repeat
// that tries a tool until it works, one that stops at its limit and lets the run continue, a group that lets the run
// continue after a failure inside it, and a step whose own condition doesn't hold on a Latitude.
internal static class TestTree
{
    public static RunScriptStep OnLatitude { get; } = Script(1, "Latitude image");

    public static RunScriptStep OnOther { get; } = Script(2, "Other image");

    public static RunScriptStep UntilItWorks { get; } = Script(3, "Install the tool");

    public static RunScriptStep Twice { get; } = Script(4, "Check the dock");

    public static RunScriptStep Failing { get; } = Script(5, "Optional tool");

    public static RunScriptStep AfterFailing { get; } = Script(6, "Another optional tool");

    public static RunScriptStep OnlyOnOptiPlex { get; } = Script(7, "Only on an OptiPlex") with
    {
        When = new TestCondition(MachineVariableNames.Model, ConditionOperator.StartsWith, "OptiPlex"),
    };

    public static IfStep PickTheImage { get; } = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000d001"),
        Name = "Pick the image",
        Test = new TestCondition(MachineVariableNames.Model, ConditionOperator.Contains, "Latitude"),
        Then = [OnLatitude],
        Else = [OnOther],
    };

    public static RepeatStep UntilTheToolWorks { get; } = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000d002"),
        Name = "Until the tool works",
        Steps = [UntilItWorks],
        Until = new TestCondition(MachineVariableNames.LastExitCode, ConditionOperator.Equals, "0"),
        MaxTimes = 3,
    };

    public static RepeatStep TryTwice { get; } = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000d003"),
        Name = "Try twice",
        Steps = [Twice],
        Until = new TestCondition(MachineVariableNames.LastExitCode, ConditionOperator.Equals, "9"),
        MaxTimes = 2,
        GoOnAtLimit = true,
    };

    public static GroupStep OptionalTools { get; } = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000d004"),
        Name = "Optional tools",
        ContinueOnError = true,
        Steps = [Failing, AfterFailing],
    };

    public static SequenceDefinition Definition { get; } =
        new(SequenceDefinition.CurrentVersion, [PickTheImage, UntilTheToolWorks, TryTwice, OptionalTools, OnlyOnOptiPlex]);

    private static RunScriptStep Script(int number, string name) => new()
    {
        Id = Guid.Parse($"0193a4b2-0000-7000-8000-00000000d{number + 100:D3}"),
        Name = name,
        Phase = SequencePhase.WindowsPE,
        Script = $"echo {number}",
    };

    // The tool fails its first time, the optional tool always, and everything else works.
    internal sealed class Runner : IStepRunner
    {
        private int _toolRuns;

        public List<Guid> Ran { get; } = [];

        public Task<StepResult> RunAsync(SequenceStep step, StepContext context, CancellationToken cancellationToken)
        {
            Ran.Add(step.Id);

            StepResult result = step.Id switch
            {
                _ when step.Id == UntilItWorks.Id => StepResult.Done() with { ExitCode = ++_toolRuns == 1 ? 1 : 0 },
                _ when step.Id == Failing.Id => StepResult.Failed("The tool failed.") with { ExitCode = 1 },
                _ => StepResult.Done() with { ExitCode = 0 },
            };

            return Task.FromResult(result);
        }
    }

    internal sealed class Store(Action<SequenceState> saved) : ISequenceStateStore
    {
        public Task SaveAsync(SequenceState state, CancellationToken cancellationToken)
        {
            saved(state);

            return Task.CompletedTask;
        }
    }
}
