// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class ConsoleStatusTests
{
    private static readonly MachineVariables s_machine =
        new("Dell Inc.", "Latitude 5440", "SN-1", "4c4c4544-0042-3510-8052-b4c04f4d3232", ["00155D010203"], null, SequencePhase.WindowsPE);

    // Every node in pre-order with where it sits, so the console's rail follows the path: a step on the branch the IF did
    // not take, or skipped after a failure inside its group, was never entered and has a pass of 0, while a step skipped
    // by its own condition was.
    [Fact]
    public async Task ShowsATreesRunAsItsNodesInPreOrder()
    {
        SequenceState start = SequenceStates.Start(TestRuns.RunId, TestTree.Definition);
        SequenceRunResult result = await new SequenceEngine(new TestTree.Runner(), new TestTree.Store(_ => { }), new Progress<StepPercent>())
            .RunAsync(start, s_machine, TestContext.Current.CancellationToken);
        ScriptedMachineConsole console = new();
        ConsoleStatus status = TestAgents.Status(console);
        AgentRun run = new(TestRuns.RunId, DeploymentState.Running, "Tree", TestTree.Definition, [], [], null, null);

        status.RunBegins(run, result.State);

        Guid ifId = TestTree.PickTheImage.Id;
        Guid groupId = TestTree.OptionalTools.Id;
        const ConsolePhase pe = ConsolePhase.WindowsPE;
        Assert.Equal(
            [
                new ConsoleStep(ifId, "Pick the image", "if", pe, ConsoleStepState.Done, null, null, 0, 1, 0, ConsoleBranch.Then),
                new ConsoleStep(TestTree.OnLatitude.Id, "Latitude image", "runScript", pe, ConsoleStepState.Done, null, ifId, 1, 1),
                new ConsoleStep(TestTree.OnOther.Id, "Other image", "runScript", pe, ConsoleStepState.Skipped, null, ifId, 1, 0),
                new ConsoleStep(TestTree.UntilTheToolWorks.Id, "Until the tool works", "repeat", pe, ConsoleStepState.Done, null, null, 0, 1, 2),
                new ConsoleStep(TestTree.UntilItWorks.Id, "Install the tool", "runScript", pe, ConsoleStepState.Done, null, TestTree.UntilTheToolWorks.Id, 1, 2),
                new ConsoleStep(TestTree.TryTwice.Id, "Try twice", "repeat", pe, ConsoleStepState.Done, null, null, 0, 1, 2),
                new ConsoleStep(TestTree.Twice.Id, "Check the dock", "runScript", pe, ConsoleStepState.Done, null, TestTree.TryTwice.Id, 1, 2),
                new ConsoleStep(groupId, "Optional tools", "group", pe, ConsoleStepState.Failed, "The tool failed.", null, 0, 1),
                new ConsoleStep(TestTree.Failing.Id, "Optional tool", "runScript", pe, ConsoleStepState.Failed, "The tool failed.", groupId, 1, 1),
                new ConsoleStep(TestTree.AfterFailing.Id, "Another optional tool", "runScript", pe, ConsoleStepState.Skipped, null, groupId, 1, 0),
                new ConsoleStep(TestTree.OnlyOnOptiPlex.Id, "Only on an OptiPlex", "runScript", pe, ConsoleStepState.Skipped, null, null, 0, 1),
            ],
            console.States[^1].Run!.Steps);
    }

    // A step without a phase of its own shows the phase of the step before it, in the tree as in a list.
    [Fact]
    public void ShowsANodeWithoutAPhaseInThePhaseBeforeIt()
    {
        GroupStep group = new()
        {
            Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a1"),
            Name = "In Windows",
            Steps = [TestRuns.Script(1, SequencePhase.Windows), TestRuns.Reboot with { Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a2") }],
        };
        SequenceDefinition definition = new(SequenceDefinition.CurrentVersion, [TestRuns.Partition, group, TestRuns.Reboot]);
        ScriptedMachineConsole console = new();

        TestAgents.Status(console).RunBegins(
            new AgentRun(TestRuns.RunId, DeploymentState.Running, "Tree", definition, [], [], null, null),
            SequenceStates.Start(TestRuns.RunId, definition));

        Assert.Equal(
            [ConsolePhase.WindowsPE, ConsolePhase.WindowsPE, ConsolePhase.Windows, ConsolePhase.Windows, ConsolePhase.Windows],
            console.States[^1].Run!.Steps.Select(step => step.Phase));
    }
}
