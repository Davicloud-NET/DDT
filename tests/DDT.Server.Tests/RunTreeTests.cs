// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using DDT.Server.Images;
using Xunit;
using static DDT.Server.Tests.TestReports;
using static DDT.Server.Tests.TreeSequences;

namespace DDT.Server.Tests;

// A run of a tree has a row for every node, and the agent's reports move each node through its visits.
public sealed class RunTreeTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private sealed record Tree(
        PartitionStep Partition,
        IfStep Choose,
        ApplyImageStep ThinkPadImage,
        ApplyImageStep OtherImage,
        GroupStep Prepare,
        RunScriptStep MayFail,
        RepeatStep Retry,
        RunScriptStep Try)
    {
        public SequenceDefinition Definition => SequenceRequests.Definition(Partition, Choose, Prepare, Retry);
    }

    // Partition, an image per model, a group that continues if its script fails, and a script repeated until it works.
    private async Task<(Tree Tree, Image ThinkPad, Image Other)> TreeAsync()
    {
        Image thinkPad = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));
        Image other = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));
        ApplyImageStep thinkPadImage = Apply(thinkPad.Id, "Apply the ThinkPad image");
        ApplyImageStep otherImage = Apply(other.Id, "Apply the other image");
        RunScriptStep mayFail = Script("Clean up");
        RunScriptStep attempt = Script("Try");

        Tree tree = new(
            Partition(),
            If([thinkPadImage], [otherImage]),
            thinkPadImage,
            otherImage,
            Group("Prepare", mayFail) with { ContinueOnError = true },
            mayFail,
            Repeat(attempt),
            attempt);

        Assert.Empty(SequenceValidator.Validate(tree.Definition));

        return (tree, thinkPad, other);
    }

    private async Task<(DeployingMachine Machine, AgentRun Run, Tree Tree, Image ThinkPad, Image Other)> AssignedAsync()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        (Tree tree, Image thinkPad, Image other) = await TreeAsync();
        SequenceView sequence = await administrator.CreatedSequenceAsync(tree.Definition);

        await administrator.AssignedAsync(machine.Id, sequence.Id);

        return (machine, (await machine.NextAsync()).Run!, tree, thinkPad, other);
    }

    private async Task<DeploymentView> ViewAsync(Guid runId) => await (await application.AdministratorAsync()).RunAsync(runId);

    private static DeploymentStepView Row(DeploymentView view, SequenceStep node) => Assert.Single(view.Steps, step => step.StepId == node.Id);

    [Fact]
    public async Task ARunOfATreeHasARowForEveryNodeAndTheFilesOfEveryBranch()
    {
        (DeployingMachine machine, AgentRun run, Tree tree, Image thinkPad, Image other) = await AssignedAsync();
        using DeployingMachine disposed = machine;

        DeploymentView view = await ViewAsync(run.Id);

        Assert.Equal(
            [
                (tree.Partition.Id, 0, (Guid?)null, 0, "partition"),
                (tree.Choose.Id, 1, null, 0, "if"),
                (tree.ThinkPadImage.Id, 2, tree.Choose.Id, 1, "applyImage"),
                (tree.OtherImage.Id, 3, tree.Choose.Id, 1, "applyImage"),
                (tree.Prepare.Id, 4, null, 0, "group"),
                (tree.MayFail.Id, 5, tree.Prepare.Id, 1, "runScript"),
                (tree.Retry.Id, 6, null, 0, "repeat"),
                (tree.Try.Id, 7, tree.Retry.Id, 1, "runScript"),
            ],
            view.Steps.Select(step => (step.StepId, step.Index, step.ParentId, step.Depth, step.Kind)));
        Assert.All(view.Steps, step =>
        {
            Assert.Equal((StepState.Pending, 0, 0, null), (step.State, step.Pass, step.Iteration, step.Branch));
            Assert.Equal(SequencePhase.WindowsPE, step.Phase);
        });

        // The list of runs counts the steps on the path. Before the IF decides, it counts the branch with more steps,
        // or the first branch if both have the same number.
        Assert.Equal(4, view.Summary.StepCount);

        // Either image may be applied, so both are the run's and the agent gets both.
        Assert.Equal(
            [(tree.ThinkPadImage.Id, thinkPad.Id), (tree.OtherImage.Id, other.Id)],
            view.Artifacts.Select(artifact => (artifact.StepId, artifact.SourceId)));
        Assert.Equal(
            new[] { thinkPad.Sha256, other.Sha256 }.Order(StringComparer.Ordinal),
            run.Images.Select(image => image.Sha256).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task EachVisitOfANodeStartsAgainAndTheRunIsDoneOverACaughtFailureAndAnUntakenBranch()
    {
        (DeployingMachine machine, AgentRun run, Tree tree, _, _) = await AssignedAsync();
        using DeployingMachine disposed = machine;
        StepRunState partitioned = Visit(tree.Partition, StepState.Done);
        TestEvaluation decided = new("test", false, "Latitude 7440");
        StepRunState chose = Visit(tree.Choose, StepState.Running) with { Branch = IfBranch.Else, Evaluation = [decided] };
        StepRunState notThinkPad = Visit(tree.ThinkPadImage, StepState.Skipped);

        await machine.ReportOkAsync(run.Id, Report(DeploymentState.Running, []) with { Activity = RunActivity.Preparing });
        await machine.ReportOkAsync(run.Id, Running(partitioned, chose, notThinkPad, Visit(tree.OtherImage, StepState.Running)));

        DeploymentView applying = await ViewAsync(run.Id);
        Assert.Equal((StepState.Running, 1, IfBranch.Else), (Row(applying, tree.Choose).State, Row(applying, tree.Choose).Pass, Row(applying, tree.Choose).Branch));
        Assert.Equal([decided], Row(applying, tree.Choose).Evaluation);
        Assert.Equal(StepState.Skipped, Row(applying, tree.ThinkPadImage).State);
        Assert.Null(Row(applying, tree.ThinkPadImage).StartedUtc);

        // The step number counts the steps on the path.
        // It doesn't count the IF that holds the image or the image it didn't choose.
        Assert.Equal((1, "Apply the other image", 4), (applying.Summary.StepIndex, applying.Summary.StepName, applying.Summary.StepCount));

        StepRunState[] imaged = [partitioned, chose with { State = StepState.Done }, notThinkPad, Visit(tree.OtherImage, StepState.Done)];
        StepRunState[] prepared = [.. imaged, Visit(tree.Prepare, StepState.Failed, error: "Clean up failed."), Visit(tree.MayFail, StepState.Failed, error: "Exit code 1.")];
        StepRunState firstTry = Visit(tree.Try, StepState.Done);

        await machine.ReportOkAsync(run.Id, Running([.. prepared, Visit(tree.Retry, StepState.Running) with { Iteration = 1 }, firstTry]));

        DeploymentStepView tried = Row(await ViewAsync(run.Id), tree.Try);
        Assert.Equal((StepState.Done, 1), (tried.State, tried.Pass));
        Assert.NotNull(tried.FinishedUtc);

        // The repeat goes round again. The script's second visit starts over, with its own times.
        StepRunState secondTry = Visit(tree.Try, StepState.Running, pass: 2);
        await machine.ReportOkAsync(run.Id, Running([.. prepared, Visit(tree.Retry, StepState.Running) with { Iteration = 2 }, secondTry]));

        DeploymentView again = await ViewAsync(run.Id);
        Assert.Equal((StepState.Running, 2, 0), (Row(again, tree.Try).State, Row(again, tree.Try).Pass, Row(again, tree.Try).Percent));
        Assert.NotNull(Row(again, tree.Try).StartedUtc);
        Assert.Null(Row(again, tree.Try).FinishedUtc);
        Assert.Equal(2, Row(again, tree.Retry).Iteration);

        // A report sent before the second visit began cannot take the step back to the first.
        HttpResponseMessage older = await machine.ReportAsync(run.Id, Running([.. prepared, Visit(tree.Retry, StepState.Running) with { Iteration = 1 }, firstTry]));
        Assert.Equal(HttpStatusCode.Conflict, older.StatusCode);
        Assert.StartsWith("Step 8, Try, is in its visit 2 and cannot go back to visit 1.", await TestDatabase.TitleAsync(older), StringComparison.Ordinal);

        // The group caught its script's failure, and the image the IF did not choose stays as it was.
        StepRunState[] finished = [.. prepared, Visit(tree.Retry, StepState.Done) with { Iteration = 2 }, Visit(tree.Try, StepState.Done, pass: 2)];
        await machine.ReportOkAsync(run.Id, Report(DeploymentState.Done, finished));

        DeploymentView done = await ViewAsync(run.Id);
        Assert.Equal(DeploymentState.Done, done.Summary.State);
        Assert.Equal((StepState.Done, 2), (Row(done, tree.Try).State, Row(done, tree.Try).Pass));
        Assert.Equal((StepState.Failed, "Exit code 1."), (Row(done, tree.MayFail).State, Row(done, tree.MayFail).Error));
    }

    [Fact]
    public async Task ARunIsNotDoneWhileANodeItReachedIsOpenOrAFailureWasNotCaught()
    {
        (DeployingMachine machine, AgentRun run, Tree tree, _, _) = await AssignedAsync();
        using DeployingMachine disposed = machine;
        StepRunState[] imaged =
        [
            Visit(tree.Partition, StepState.Done),
            Visit(tree.Choose, StepState.Done) with { Branch = IfBranch.Then },
            Visit(tree.ThinkPadImage, StepState.Done),
        ];

        await machine.ReportOkAsync(run.Id, Running(imaged));

        // The image of the other branch was never reached and may stay pending. The steps after the IF may not.
        HttpResponseMessage open = await machine.ReportAsync(
            run.Id,
            Report(DeploymentState.Done, [.. imaged, Visit(tree.Prepare, StepState.Done), Visit(tree.MayFail, StepState.Done)]));
        Assert.Equal(HttpStatusCode.Conflict, open.StatusCode);
        Assert.StartsWith("Step 7, Until it works, is pending, so the run is not done.", await TestDatabase.TitleAsync(open), StringComparison.Ordinal);

        HttpResponseMessage running = await machine.ReportAsync(
            run.Id,
            Report(DeploymentState.Done, [.. imaged, Visit(tree.Prepare, StepState.Done), Visit(tree.MayFail, StepState.Done), Visit(tree.Retry, StepState.Done), Visit(tree.Try, StepState.Running)]));
        Assert.Equal(HttpStatusCode.Conflict, running.StatusCode);
        Assert.StartsWith("Step 8, Try, is running, so the run is not done.", await TestDatabase.TitleAsync(running), StringComparison.Ordinal);

        // Nothing above the repeat's script continues after its failure.
        HttpResponseMessage uncaught = await machine.ReportAsync(
            run.Id,
            Report(
                DeploymentState.Done,
                [.. imaged, Visit(tree.Prepare, StepState.Done), Visit(tree.MayFail, StepState.Done), Visit(tree.Retry, StepState.Failed), Visit(tree.Try, StepState.Failed, pass: 2)]));
        Assert.Equal(HttpStatusCode.Conflict, uncaught.StatusCode);
        Assert.StartsWith("Step 7, Until it works, failed and must not fail", await TestDatabase.TitleAsync(uncaught), StringComparison.Ordinal);
        Assert.Equal(DeploymentState.Running, (await ViewAsync(run.Id)).Summary.State);
    }

    // The tests of a decision come from outside, so the server bounds them to what the engine keeps of them.
    [Fact]
    public async Task BoundsTheTestsOfADecision()
    {
        (DeployingMachine machine, AgentRun run, Tree tree, _, _) = await AssignedAsync();
        using DeployingMachine disposed = machine;
        TestEvaluation[] many = [.. Enumerable.Range(0, 40).Select(index => new TestEvaluation($"test.parts[{index}]\0", true, "\0" + new string('x', 400)))];

        await machine.ReportOkAsync(run.Id, Running(Visit(tree.Partition, StepState.Done), Visit(tree.Choose, StepState.Running) with { Branch = IfBranch.Then, Evaluation = many }));

        IReadOnlyList<TestEvaluation> kept = Row(await ViewAsync(run.Id), tree.Choose).Evaluation!;
        Assert.Equal(TestEvaluation.MaxPerNode, kept.Count);
        Assert.Equal("test.parts[0]", kept[0].Path);
        Assert.Equal(new string('x', TestEvaluation.MaxActualLength), kept[0].Actual);

        HttpResponseMessage negative = await machine.ReportAsync(run.Id, Running(Visit(tree.Partition, StepState.Done), Visit(tree.Choose, StepState.Running, pass: -1)));
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
    }
}
