// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class RunHeartbeatTests
{
    private static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");
    private static readonly SequenceDefinition s_definition = new(SequenceDefinition.CurrentVersion, TestRuns.InstallWindows);

    private readonly ScriptedAgentServer _server = new();
    private readonly DeploymentTokens _tokens = new("session-0", "resume-0");
    private readonly List<string?> _savedTokens = [];
    private readonly RunHeartbeat _heartbeat;

    public RunHeartbeatTests()
    {
        ImmediateTimeProvider time = new();
        _heartbeat = new RunHeartbeat(
            _server,
            new AgentLog(time, TextWriter.Null),
            _tokens,
            s_machineId,
            TestRuns.RunId,
            _ =>
            {
                _savedTokens.Add(_tokens.RunToken);

                return Task.CompletedTask;
            },
            Timeout.InfiniteTimeSpan,
            time);
    }

    [Fact]
    public void ReportsTheStepsThatLeftPendingAndTheRunningStepsPercent()
    {
        _heartbeat.Update(State(StepState.Done, StepState.Running, StepState.Pending));
        _heartbeat.Report(new StepPercent(TestRuns.Apply.Id, 45));
        _heartbeat.Report(new StepPercent(TestRuns.Partition.Id, 90));
        _heartbeat.Activity = RunActivity.Step;

        AgentRunReport report = _heartbeat.Snapshot(DeploymentState.Running);

        Assert.Equal(
            [new StepRunState(TestRuns.Partition.Id, StepState.Done, null), new StepRunState(TestRuns.Apply.Id, StepState.Running, null)],
            report.Steps);
        Assert.Equal((TestRuns.Apply.Id, 45, RunActivity.Step, SequencePhase.WindowsPE), (report.CurrentStepId, report.Percent, report.Activity, report.Phase));
    }

    [Fact]
    public void StartsTheNextStepAtNoPercent()
    {
        _heartbeat.Update(State(StepState.Done, StepState.Running, StepState.Pending));
        _heartbeat.Report(new StepPercent(TestRuns.Apply.Id, 45));
        _heartbeat.Update(State(StepState.Done, StepState.Done, StepState.Running));

        AgentRunReport report = _heartbeat.Snapshot(DeploymentState.Running);

        Assert.Equal((TestRuns.Unattend.Id, 0), (report.CurrentStepId, report.Percent));
    }

    // The console at the machine reads the position after each change, and tells a step that has not said how far it is
    // from one at 0 %.
    [Fact]
    public void SaysWhenTheRunMovesAndWhetherTheStepsPercentIsKnown()
    {
        int changes = 0;
        _heartbeat.Changed += () => changes++;

        _heartbeat.Update(State(StepState.Done, StepState.Running, StepState.Pending));

        Assert.Equal((TestRuns.Apply.Id, (int?)null, 1), (_heartbeat.Position.StepId, _heartbeat.Position.Percent, changes));

        _heartbeat.Report(new StepPercent(TestRuns.Apply.Id, 0));
        _heartbeat.Report(new StepPercent(TestRuns.Apply.Id, 0));
        _heartbeat.Report(new StepPercent(TestRuns.Apply.Id, 45));
        _heartbeat.Report(new StepPercent(TestRuns.Partition.Id, 90));

        Assert.Equal(((int?)45, 3), (_heartbeat.Position.Percent, changes));

        _heartbeat.Activity = RunActivity.Step;
        _heartbeat.Activity = RunActivity.Step;
        _heartbeat.Update(State(StepState.Done, StepState.Done, StepState.Running));

        Assert.Equal(
            (TestRuns.Unattend.Id, (int?)null, RunActivity.Step, 5),
            (_heartbeat.Position.StepId, _heartbeat.Position.Percent, _heartbeat.Position.Activity, changes));
    }

    [Fact]
    public void AFailedReportNamesTheRunningStepAsTheOneThatFailed()
    {
        _heartbeat.Update(State(StepState.Done, StepState.Running, StepState.Pending));

        AgentRunReport report = _heartbeat.Snapshot(DeploymentState.Failed, "The server stopped the run.");

        Assert.Equal(new StepRunState(TestRuns.Apply.Id, StepState.Failed, "The server stopped the run."), report.Steps[1]);
        Assert.Equal("The server stopped the run.", report.Error);
    }

    // The group and the repeat around the step that runs are Running too, and come first in the tree.
    [Fact]
    public void TheCurrentStepOfATreeIsTheLeafThatRuns()
    {
        RepeatStep repeat = new()
        {
            Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a2"),
            Name = "Try again",
            Steps = [TestRuns.Script(1)],
            Until = new TestCondition(MachineVariableNames.LastExitCode, ConditionOperator.Equals, "0"),
        };
        GroupStep group = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a1"), Name = "Tools", Steps = [repeat] };
        SequenceState state = SequenceStates.Start(TestRuns.RunId, new SequenceDefinition(SequenceDefinition.CurrentVersion, [group]));

        _heartbeat.Update(state with { Steps = [.. state.Steps.Select(step => step with { State = StepState.Running, Pass = 1 })] });

        Assert.Equal(TestRuns.Script(1).Id, _heartbeat.Snapshot(DeploymentState.Running).CurrentStepId);

        _heartbeat.Update(state with { Steps = [.. state.Steps.Take(2).Select(step => step with { State = StepState.Running, Pass = 1 }), state.Steps[2]] });

        Assert.Null(_heartbeat.Snapshot(DeploymentState.Running).CurrentStepId);
    }

    [Fact]
    public async Task KeepsTheNewestRunTokenAndHasItWrittenToTheDisk()
    {
        _server.OnRunReport(DeploymentState.Running, _ => new AgentRunReportResult("session-1", "resume-1", "run-token-1"))
            .OnRunReport(DeploymentState.Running, _ => new AgentRunReportResult("session-2", "resume-2", null));

        await _heartbeat.ReportNowAsync(TestContext.Current.CancellationToken);
        await _heartbeat.ReportNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["run-report Running session-0", "run-report Running session-1"], _server.Calls);
        Assert.Equal(("session-2", "run-token-1"), (_tokens.Token, _tokens.RunToken));
        Assert.Equal(["run-token-1"], _savedTokens);
    }

    [Theory]
    [InlineData(DeploymentState.Done)]
    [InlineData(DeploymentState.Failed)]
    public async Task WritesNoRunTokenOnceTheRunIsOver(DeploymentState state)
    {
        _server.OnRunReport(state, _ => new AgentRunReportResult("session-1", "resume-1", "run-token-2"));

        await _heartbeat.ReportAsync(_heartbeat.Snapshot(state), TestContext.Current.CancellationToken);

        Assert.Empty(_savedTokens);
    }

    // Quick steps change the run many times a second, and the server takes only so many calls a minute from a machine.
    [Fact]
    public async Task ChangesSoonAfterABeatWaitForTheSpacingAndShareTheNextBeat()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ManualTimeProvider time = new();
        RunHeartbeat heartbeat = new(
            _server,
            new AgentLog(time, TextWriter.Null),
            _tokens,
            s_machineId,
            TestRuns.RunId,
            _ => Task.CompletedTask,
            TimeSpan.FromSeconds(10),
            time);
        using CancellationTokenSource run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        heartbeat.Update(State(StepState.Running));
        heartbeat.Start(run, cancellationToken);
        await WaitForAsync(() => _server.RunReports.Count == 1 && time.HasTimerDueIn(TimeSpan.FromSeconds(10)));

        heartbeat.Update(State(StepState.Done, StepState.Running));
        heartbeat.Update(State(StepState.Done, StepState.Done, StepState.Running));
        await WaitForAsync(() => _server.RunReports.Count > 1 || time.HasTimerDueIn(RunHeartbeat.MinimumSpacing));
        Assert.Single(_server.RunReports);

        time.Advance(RunHeartbeat.MinimumSpacing);
        await WaitForAsync(() => _server.RunReports.Count == 2);
        Assert.Equal([StepState.Done, StepState.Done, StepState.Running], _server.RunReports[1].Steps.Select(step => step.State));

        await heartbeat.StopAsync();
    }

    // For what happens on the heartbeat's own loop, which a test cannot await.
    private static async Task WaitForAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static SequenceState State(params StepState[] states) =>
        SequenceStates.Start(TestRuns.RunId, s_definition) with
        {
            Steps = [.. s_definition.Steps.Zip(states, (step, state) => new StepRunState(step.Id, state, null))],
        };
}
