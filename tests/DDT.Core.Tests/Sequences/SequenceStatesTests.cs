// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Core.Tests.Sequences;

public sealed class SequenceStatesTests
{
    [Fact]
    public void StartsATreeAsFormat2AtItsFirstNode()
    {
        RunScriptStep inside = Script("Inside");
        GroupStep group = new() { Id = Guid.NewGuid(), Name = "Group", Steps = [inside] };
        RebootStep after = Reboot("After");

        SequenceState state = SequenceStates.Start(Guid.NewGuid(), Tree(group, after));

        Assert.Equal(SequenceState.TreeFormat, state.Format);
        Assert.Equal(SequenceStates.NoNextIndex, state.NextIndex);
        Assert.Equal(new NodeCursor(group.Id, false), state.Cursor);
        Assert.Equal([group.Id, inside.Id, after.Id], state.Steps.Select(step => step.StepId));
        Assert.All(state.Steps, step => Assert.Equal(new StepRunState(step.StepId, StepState.Pending, null), step));

        // A run with nothing to do is at its end from the start.
        SequenceState empty = SequenceStates.Start(Guid.NewGuid(), Tree() with { Variables = [new VariableDeclaration { Name = "Office" }] });

        Assert.Equal(SequenceState.TreeFormat, empty.Format);
        Assert.Null(empty.Cursor);
    }

    // Whatever version it says, a document an agent of version 2 could run starts as Format 1; one test of version 3
    // makes it a tree's.
    [Fact]
    public void StartsAFlatDocumentAsFormat1UnlessItUsesVersion3()
    {
        RunScriptStep script = Script("Script");
        RunScriptStep tested = Script("Tested") with
        {
            When = new TestCondition(MachineVariableNames.TpmPresent, ConditionOperator.Equals, "yes"),
        };

        Assert.Equal(SequenceState.CurrentFormat, SequenceStates.Start(Guid.NewGuid(), new SequenceDefinition(2, [script])).Format);
        Assert.Equal(SequenceState.CurrentFormat, SequenceStates.Start(Guid.NewGuid(), new SequenceDefinition(3, [script])).Format);
        Assert.Null(SequenceStates.Start(Guid.NewGuid(), new SequenceDefinition(3, [script])).Cursor);
        Assert.Equal(SequenceState.TreeFormat, SequenceStates.Start(Guid.NewGuid(), new SequenceDefinition(3, [script, tested])).Format);
    }

    [Fact]
    public void UpgradesAFormat1StateToACursorAtItsNextIndex()
    {
        RunScriptStep first = Script("First");
        RunScriptStep second = Script("Second");
        RunScriptStep third = Script("Third");
        SequenceState flat = SequenceStates.Start(Guid.NewGuid(), new SequenceDefinition(2, [first, second, third])) with
        {
            NextIndex = 1,
            Steps =
            [
                new StepRunState(first.Id, StepState.Skipped, null),
                new StepRunState(second.Id, StepState.Running, null),
                new StepRunState(third.Id, StepState.Pending, null),
            ],
        };

        SequenceState upgraded = SequenceStates.Upgrade(flat);

        Assert.Equal(SequenceState.TreeFormat, upgraded.Format);
        Assert.Equal(new NodeCursor(second.Id, false), upgraded.Cursor);
        Assert.Equal([1, 1, 0], upgraded.Steps.Select(step => step.Pass));
        Assert.Equal(flat.Steps.Select(step => step.State), upgraded.Steps.Select(step => step.State));
        Assert.Null(SequenceStates.Upgrade(flat with { NextIndex = 3 }).Cursor);
        Assert.Same(upgraded, SequenceStates.Upgrade(upgraded));
    }

    private static SequenceDefinition Tree(params SequenceStep[] steps) => new(3, steps);

    private static RunScriptStep Script(string name) =>
        new() { Id = Guid.NewGuid(), Name = name, Phase = SequencePhase.WindowsPE, Script = "exit /b 0" };

    private static RebootStep Reboot(string name) => new() { Id = Guid.NewGuid(), Name = name };
}
