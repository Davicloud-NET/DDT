// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Accounts;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Core.Sequences;
using DDT.Server.Data;
using Xunit;

namespace DDT.E2E;

// The real host and the published agent in dry runs of what a sequence of version 3 does: a tree of IFs, repeats and
// pauses, the variables its steps set, the values rules give a run, the inputs a run asks, and a step's shares with a
// stored account, which a dry run only names.
[Trait("Category", "E2E")]
[Collection(DryRunCollection.Name)]
public sealed class TreeDryRunTests(DryRunLab lab)
{
    // The agent's exit code for a run that finished.
    private const int Deployed = 4;

    // Set variable has no sums, so the count of tries is written out: 0, then 0+1 on the first try, 0+1+1 on the second.
    private const string Tries = "Tries";
    private const string FirstTry = "0+1";

    private const string ShareHost = "files.e2e.ddt.test";
    private const string ShareUser = @"E2E\svc-share";

    private static readonly TimeSpan s_runTimeout = TimeSpan.FromMinutes(3);

    // Partition; an IF on the model a person knows, whose Then applies the lab's image and names the PC and whose Else
    // applies another; a repeat that restarts Windows PE before its first try, which fails, and tries again until the
    // last step worked; the answer file; the hand-over; a pause in Windows that the web continues; and a script that runs
    // as a stored account with a share of it connected.
    [Fact(Timeout = 600_000)]
    public async Task ATreeTakesItsBranchTriesAgainAfterARestartAndPausesInWindowsUntilTheWebContinuesIt()
    {
        lab.SkipWhenUnavailable();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        AccountView account = await lab.CreateAccountAsync(ShareUser, DryRunLab.Domain, [ShareHost], runAs: true, cancellationToken);
        AccountReference stored = new(account.Id, null);
        TestCondition onFirstTry = new(Tries, ConditionOperator.Equals, FirstTry);

        PartitionStep partition = DryRunTests.Partition();
        ApplyImageStep applyLab = new() { Id = Guid.CreateVersion7(), Name = "Apply the lab's image", ImageId = lab.Image.Id };
        SetVariableStep name = new()
        {
            Id = Guid.CreateVersion7(),
            Name = "Name it for the lab",
            Variable = MachineVariableNames.ComputerName,
            Value = "LAB-{{SerialNumber|right:5}}",
        };
        ApplyImageStep applyOther = new() { Id = Guid.CreateVersion7(), Name = "Apply the other image", ImageId = lab.OtherImage.Id };
        IfStep byModel = new()
        {
            Id = Guid.CreateVersion7(),
            Name = "By model",
            Test = new TestCondition(MachineVariableNames.FriendlyModel, ConditionOperator.Equals, DryRunLab.Model),
            Then = [applyLab, name],
            Else = [applyOther],
        };

        // A dry run takes every script's exit code as 0, so the first try fails by accepting only 1, and the second works.
        // Each runs only on its own try, which the count says.
        SetVariableStep count = new() { Id = Guid.CreateVersion7(), Name = "Count the try", Variable = Tries, Value = "{{Tries}}+1" };
        RebootStep restart = new() { Id = Guid.CreateVersion7(), Name = "Restart before the first try", When = onFirstTry };
        RunScriptStep firstTry = DryRunTests.Script("Install the tool", SequencePhase.WindowsPE, "setup.exe /quiet") with
        {
            When = onFirstTry,
            SuccessExitCodes = [1],
            ContinueOnError = true,
        };
        RunScriptStep secondTry = DryRunTests.Script("Install the tool again", SequencePhase.WindowsPE, "setup.exe /quiet") with
        {
            When = new TestCondition(Tries, ConditionOperator.NotEquals, FirstTry),
        };
        RepeatStep untilInstalled = new()
        {
            Id = Guid.CreateVersion7(),
            Name = "Until the tool is installed",
            Steps = [count, restart, firstTry, secondTry],
            Until = new AllCondition
            {
                Parts =
                [
                    new TestCondition(MachineVariableNames.LastStepFailed, ConditionOperator.Equals, "No"),
                    new TestCondition(MachineVariableNames.LastExitCode, ConditionOperator.Equals, "0"),
                ],
            },
            MaxTimes = 3,
        };

        WriteUnattendStep unattend = new() { Id = Guid.CreateVersion7(), Name = "Write the answer file", LocalAdministrator = true };
        RunScriptStep inWindows = DryRunTests.Script("First in Windows", SequencePhase.Windows, "echo %DDT_PHASE%");
        PauseStep pause = new() { Id = Guid.CreateVersion7(), Name = "Check the machine", Message = "Check {{ComputerName}} before the drivers are copied." };
        RunScriptStep copy = DryRunTests.Script("Copy the drivers", SequencePhase.Windows, $@"robocopy \\{ShareHost}\drivers C:\Drivers /e") with
        {
            Shares = [new ShareConnection($@"\\{ShareHost}\drivers", stored)],
            RunAs = stored,
        };

        SequenceView sequence = await lab.CreateSequenceAsync(
            "Tree",
            new SequenceDefinition(SequenceDefinition.CurrentVersion, [partition, byModel, untilInstalled, unattend, inWindows, pause, copy])
            {
                Variables =
                [
                    new VariableDeclaration { Name = MachineVariableNames.ComputerName, Default = "PC-{{SerialNumber|right:5}}", SetBySteps = true },
                    new VariableDeclaration { Name = Tries, Default = "0", SetBySteps = true },
                ],
            },
            cancellationToken);
        Assert.Equal(SequenceDefinition.CurrentVersion, sequence.Definition.Version);

        await using AgentProcess agent = lab.StartAgent();
        (Guid machineId, Guid runId) = await AuthorizeAsync(agent, sequence, cancellationToken);
        string labName = $"LAB-{agent.SerialNumber[^5..]}";

        // The run waits at the pause in Windows with its message worked out, and nothing after it runs meanwhile.
        DeploymentView paused = await WaitForRunAsync(
            agent,
            runId,
            run => run.Summary is { Waiting: true, Activity: RunActivity.Paused },
            "The pause in Windows",
            cancellationToken);
        string message = $"Check {labName} before the drivers are copied.";
        Assert.Equal((DeploymentState.Running, SequencePhase.Windows, message), (paused.Summary.State, paused.Summary.Phase, paused.Summary.PauseMessage));
        Assert.Equal((pause.Id, 1, message, (DateTimeOffset?)null), (paused.Pause!.StepId, paused.Pause.Pass, paused.Pause.Message, paused.Pause.ContinuesUtc));
        Assert.Equal((StepState.Running, StepState.Pending), (Row(paused, pause.Id).State, Row(paused, copy.Id).State));

        DeploymentView continued = await lab.ContinueAsync(machineId, paused.Pause, cancellationToken);
        Assert.Equal((false, null, null), (continued.Summary.Waiting, continued.Summary.PauseMessage, continued.Pause));

        Assert.Equal(Deployed, await agent.WaitForExitAsync(s_runTimeout, cancellationToken));
        DryRunTests.AssertRemovedItself(agent);
        Assert.Equal(1, agent.Output.Count("Someone continued the run on the web."));

        DeploymentView run = await lab.RunAsync(runId, cancellationToken);
        Assert.Equal((DeploymentState.Done, null, false), (run.Summary.State, run.Summary.Error, run.Summary.Waiting));

        // A row per node in the tree's order, with the latest visit of each: the branch the IF took and the one it skipped,
        // the repeat's second time through, whose first try and restart were skipped, and the steps after it.
        Assert.Equal(
            [
                (partition.Id, null, 0, SequencePhase.WindowsPE, StepState.Done, 1, 0, null),
                (byModel.Id, null, 0, SequencePhase.WindowsPE, StepState.Done, 1, 0, IfBranch.Then),
                (applyLab.Id, byModel.Id, 1, SequencePhase.WindowsPE, StepState.Done, 1, 0, null),
                (name.Id, byModel.Id, 1, SequencePhase.WindowsPE, StepState.Done, 1, 0, null),
                (applyOther.Id, byModel.Id, 1, SequencePhase.WindowsPE, StepState.Skipped, 1, 0, null),
                (untilInstalled.Id, null, 0, SequencePhase.WindowsPE, StepState.Done, 1, 2, null),
                (count.Id, untilInstalled.Id, 1, SequencePhase.WindowsPE, StepState.Done, 2, 0, null),
                (restart.Id, untilInstalled.Id, 1, SequencePhase.WindowsPE, StepState.Skipped, 2, 0, null),
                (firstTry.Id, untilInstalled.Id, 1, SequencePhase.WindowsPE, StepState.Skipped, 2, 0, null),
                (secondTry.Id, untilInstalled.Id, 1, SequencePhase.WindowsPE, StepState.Done, 2, 0, null),
                (unattend.Id, null, 0, SequencePhase.WindowsPE, StepState.Done, 1, 0, null),
                (inWindows.Id, null, 0, SequencePhase.Windows, StepState.Done, 1, 0, null),
                (pause.Id, null, 0, SequencePhase.Windows, StepState.Done, 1, 0, null),
                (copy.Id, null, 0, SequencePhase.Windows, StepState.Done, 1, 0, (IfBranch?)null),
            ],
            run.Steps.Select(step => (step.StepId, step.ParentId, step.Depth, step.Phase, step.State, step.Pass, step.Iteration, step.Branch)));

        // What each decision tested, and the value it found.
        Assert.Equal([new TestEvaluation(ConditionEvaluator.TestPath, true, DryRunLab.Model)], Row(run, byModel.Id).Evaluation);
        Assert.Equal(
            [new TestEvaluation($"{ConditionEvaluator.UntilPath}.parts[0]", true, "No"), new TestEvaluation($"{ConditionEvaluator.UntilPath}.parts[1]", true, "0")],
            Row(run, untilInstalled.Id).Evaluation);
        Assert.Equal([new TestEvaluation(ConditionEvaluator.WhenPath, false, "0+1+1")], Row(run, firstTry.Id).Evaluation);
        Assert.Equal([new TestEvaluation(ConditionEvaluator.WhenPath, true, "0+1+1")], Row(run, secondTry.Id).Evaluation);
        Assert.All(run.Steps.Where(step => step.State == StepState.Done), step => Assert.True(step.StartedUtc <= step.FinishedUtc, $"{step.Name} has no times."));
        Assert.All(run.Steps, step => Assert.Contains(lab.Live.StepPushes(machineId), pushed => (pushed.StepId, pushed.State, pushed.Pass) == (step.StepId, step.State, step.Pass)));

        // The first try failed, the repeat went through its steps again after it, and the second try ended it.
        Assert.Equal(1, agent.Output.Count($"Step {firstTry.Name} failed after"));
        Assert.Equal(1, agent.Output.Count($"Repeat {untilInstalled.Name} runs its steps again (2 of at most 3 times)"));

        // Only the image of the branch the IF took was applied.
        Assert.Equal(1, agent.Output.Count($"Applying image 1 of {lab.Image.Name} to "));
        Assert.Equal(0, agent.Output.Count(lab.OtherImage.Name));

        // The name the IF's branch set went into the answer file and the pause's message, and it and the count of tries
        // reached the run and the page that watched the machine; the values the run started with stay as they were.
        Assert.Equal((labName, "0+1+1"), (run.Variables![MachineVariableNames.ComputerName], run.Variables[Tries]));
        Assert.Contains(lab.Live.VariablePushes(machineId), pushed => pushed.GetValueOrDefault(MachineVariableNames.ComputerName) == labName);
        Assert.Equal(
            [($"PC-{agent.SerialNumber[^5..]}", ValueSource.SequenceDefault), ("0", ValueSource.SequenceDefault)],
            run.Values!.Where(value => value.Name is MachineVariableNames.ComputerName or Tries).Select(value => (value.Value, value.Source)));
        Assert.Equal(1, agent.Output.Count($"Wrote the answer file: computer name {labName}, time zone W. Europe Standard Time, "));

        // The script ran as the stored account with its share, which the dry run only names, from what the server handed
        // out for the step while it ran.
        Assert.Equal(1, agent.Output.Count($"Dry run: {ShareUser} is not signed in."));
        Assert.Equal(1, agent.Output.Count($@"Dry run: not connected: \\{ShareHost}\drivers as {ShareUser}, for {ShareUser}."));
        Assert.Equal(1, agent.Output.Count($"Dry run: not run as {ShareUser} "));

        // Restarted once in Windows PE and handed over once; the answer file and the step's account were read once for
        // what each was for, and the pause was continued by the administrator.
        IReadOnlyList<AuditEvent> audit = await lab.AuditAsync(runId, cancellationToken);
        Assert.Equal(["WindowsPE", "Windows"], audit.Where(entry => entry.Action == "deployment.resumed").OrderBy(entry => entry.Id).Select(DryRunTests.ResumedIn));
        string[] reads = [.. audit.Where(entry => entry.Action == "deployment.secret-read").OrderBy(entry => entry.Id).Select(entry => entry.Detail ?? "")];
        string named = $"The account {account.Name} ({account.Id:D}) of step {copy.Name} ({copy.Id:D}) of {sequence.Name}";
        Assert.Equal(3, reads.Length);
        Assert.StartsWith($"The answer file of step {unattend.Name} ({unattend.Id:D})", reads[0], StringComparison.Ordinal);
        Assert.Equal([$"{named}, to run the script as {ShareUser}.", $@"{named}, for \\{ShareHost}\drivers."], reads[1..]);
        AuditEvent continuedBy = Assert.Single(audit, entry => entry.Action == "deployment.continued");
        Assert.Equal("admin", continuedBy.ActorName);

        MachineLogPage log = await lab.LogAsync(machineId, $"deploymentId={runId:D}&limit=1000", cancellationToken);
        lab.AssertClean(
            [agent],
            [$"Step {firstTry.Name} failed after"],
            ("The paused run's detail", JsonSerializer.Serialize(paused, DdtJsonContext.Default.DeploymentView)),
            ("The run's detail", JsonSerializer.Serialize(run, DdtJsonContext.Default.DeploymentView)),
            ("The run's audit", string.Join(Environment.NewLine, audit.Select(entry => entry.Detail))),
            ("The run's log", string.Join(Environment.NewLine, log.Lines.Select(line => line.Message))));
    }

    // Approved and assigned on the web, the machine watched first so no push about its run is missed. Returns the machine
    // and its run.
    private async Task<(Guid MachineId, Guid RunId)> AuthorizeAsync(AgentProcess agent, SequenceView sequence, CancellationToken cancellationToken)
    {
        MachineSummary machine = await lab.WaitForMachineAsync(agent, cancellationToken);
        await lab.Live.WatchAsync(machine.Id, cancellationToken);
        await lab.ApproveAsync(machine.Id, null, cancellationToken);
        MachineSummary assigned = await lab.AssignAsync(machine.Id, sequence.Id, null, cancellationToken);

        return (machine.Id, assigned.Deployment!.Id);
    }

    // Until the run is as wanted, failing at once should it end otherwise.
    private Task<DeploymentView> WaitForRunAsync(
        AgentProcess agent,
        Guid runId,
        Func<DeploymentView, bool> wanted,
        string what,
        CancellationToken cancellationToken) =>
        Eventually.GetAsync(
            what,
            s_runTimeout,
            async call =>
            {
                DeploymentView run = await lab.RunAsync(runId, call);

                if (wanted(run))
                {
                    return run;
                }

                return run.Summary.State is DeploymentState.Done or DeploymentState.Failed or DeploymentState.Cancelled
                    ? throw new InvalidOperationException(
                        $"{what} did not happen: the run ended {run.Summary.State} ({run.Summary.Error}).{Environment.NewLine}{agent.Output.Tail()}")
                    : null;
            },
            () => agent.Output.Tail(),
            cancellationToken);

    private static DeploymentStepView Row(DeploymentView run, Guid stepId) => run.Steps.Single(step => step.StepId == stepId);
}
