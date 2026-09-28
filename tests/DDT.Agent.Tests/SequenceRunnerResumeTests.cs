// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// A run that goes on from its state on the disk after Windows PE restarted.
public sealed class SequenceRunnerResumeTests : SequenceRunnerTestBase
{
    [Fact]
    public async Task RestartsIntoWindowsPEAndGoesOnFromTheDisk()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        FakeUefiVariables firmware = new();
        firmware.Values["BootCurrent"] = [0x03, 0x00];
        firmware.Values["Boot0003"] = [0x01];
        ImmediateTimeProvider time = new();
        AgentLog log = new(time, TextWriter.Null);
        AgentRun run = TestRuns.Run([TestRuns.Partition, TestRuns.Script(1), TestRuns.Reboot, TestRuns.Apply, TestRuns.Unattend], _image);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRunReport(DeploymentState.Running, _ => new AgentRunReportResult("session-1", "resume-1", "run-token-1"));

        RunResult first = await RunAsync(server, run, new() { Time = time, Log = log, Rebooter = new WindowsPERebooter(_toolRunner, firmware, log) });

        Assert.Equal(RunOutcome.Restarting, first.Outcome);
        Assert.Equal(["list", "prepare", "partition 0"], _tools.Calls);
        Assert.Equal(RestartInto.WindowsPE, RestartDue());
        Assert.Equal(["BootNext"], firmware.Writes);
        Assert.EndsWith("wpeutil.exe reboot", _toolRunner.Calls[^1], StringComparison.Ordinal);
        Assert.Equal(RunActivity.Restarting, server.RunReports[^1].Activity);

        // The next start of Windows PE: only the disk remembers the run.
        LocalRun? found = await LocalRun.LoadAsync(Windows, log, cancellationToken);
        Assert.NotNull(found);
        Assert.Equal((3, "run-token-1"), (found.State.NextIndex, found.RunToken));
        int reportsBefore = server.RunReports.Count;

        RunResult second = await RunAsync(server, run with { State = DeploymentState.Running }, new() { Resumed = found, RunToken = found.RunToken });

        Assert.Equal(RunOutcome.Finished, second.Outcome);
        Assert.Equal(
            ["list", "prepare", "partition 0", $"find {FakeDeploymentTools.WindowsPartitionId}", "apply 1", "bcd", "firmware after the answer file", "reboot"],
            _tools.Calls);
        Assert.Single(_toolRunner.Calls, call => call.EndsWith("c001.cmd", StringComparison.Ordinal));
        Assert.Equal([StepState.Done, StepState.Done, StepState.Done], server.RunReports[reportsBefore].Steps.Take(3).Select(step => step.State));
    }

    // The steps the engine settles without running them reach the machine's log once each: the first start skips one
    // and is stopped in the next, and the start after it fails that step as interrupted. With Continue on error the run
    // goes on to its restart, and the start after that one finishes it.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LogsASkippedAndAnInterruptedStepOnceAcrossRestarts(bool continueOnError)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RunScriptStep skipped = TestRuns.Script(1) with
        {
            Conditions = [new StepCondition(MachineVariableNames.Model, ConditionOperator.StartsWith, "OptiPlex")],
        };
        AgentRun run = TestRuns.Run([TestRuns.Partition, skipped, TestRuns.Script(2) with { ContinueOnError = continueOnError }, TestRuns.Reboot, TestRuns.Script(3)]);
        ScriptedAgentServer first = new();
        _toolRunner.AnswerExitCode = (_, arguments, _) =>
        {
            if (arguments.Any(argument => argument.EndsWith("c002.cmd", StringComparison.Ordinal)))
            {
                first.Stop.Cancel();
                first.Stop.Token.ThrowIfCancellationRequested();
            }

            return 0;
        };
        const string skipLine = "INFO  Step Script 1 was skipped, because this condition did not hold: Model starts with \"OptiPlex\", and the machine reports \"Dry run\".";
        string interrupted = $"ERROR Step Script 2 failed: {SequenceEngine.InterruptedError}" +
            (continueOnError ? " The run goes on, because \"Go on when this step fails\" is on for this step." : string.Empty);

        StringWriter firstConsole = new();
        Assert.Equal(RunOutcome.Stopped, (await RunAsync(first, run, new() { Log = new AgentLog(new ImmediateTimeProvider(), firstConsole) })).Outcome);

        StringWriter secondConsole = new();
        LocalRun? found = await LocalRun.LoadAsync(Windows, Log(), cancellationToken);
        RunResult second = await RunAsync(new ScriptedAgentServer(), run with { State = DeploymentState.Running }, new() { Resumed = found, Log = new AgentLog(new ImmediateTimeProvider(), secondConsole) });

        Assert.Equal(continueOnError ? RunOutcome.Restarting : RunOutcome.Failed, second.Outcome);
        Assert.Single(Lines(firstConsole), line => line == skipLine);
        Assert.DoesNotContain(Lines(firstConsole), line => line.StartsWith("ERROR", StringComparison.Ordinal));
        Assert.DoesNotContain(skipLine, Lines(secondConsole));
        Assert.Single(Lines(secondConsole), line => line == interrupted);
        Assert.Equal(!continueOnError, Lines(secondConsole).Contains($"ERROR The run failed: {SequenceEngine.InterruptedError}"));

        if (continueOnError)
        {
            StringWriter thirdConsole = new();
            found = await LocalRun.LoadAsync(Windows, Log(), cancellationToken);
            RunResult third = await RunAsync(new ScriptedAgentServer(), run with { State = DeploymentState.Running }, new() { Resumed = found, Log = new AgentLog(new ImmediateTimeProvider(), thirdConsole) });

            Assert.Equal(RunOutcome.Finished, third.Outcome);
            Assert.DoesNotContain(Lines(thirdConsole), line => line == skipLine || line == interrupted);
            Assert.Single(_toolRunner.Calls, call => call.EndsWith("c003.cmd", StringComparison.Ordinal));
        }
    }

    // The engine asked for the restart, but the server ended the run before it, so no restart is due any more.
    [Fact]
    public async Task ARunThatFailsAfterItsRestartStepOwesNoRestart()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());
        OnFirstBeatOnceTheRestartIsDue(server, () => throw new AgentRequestException("409", "This run is no longer running.", HttpStatusCode.Conflict));

        RunResult result = await RunAsync(server, TestRuns.Run([TestRuns.Partition, TestRuns.Reboot, TestRuns.Apply, TestRuns.Unattend], _image));

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal((DeploymentState.Failed, "This run is no longer running."), (server.RunReports[^1].State, server.RunReports[^1].Error));
        Assert.Equal(["list", "prepare", "partition 0"], _tools.Calls);
        Assert.Null(RestartDue());
    }

    // Ctrl+C once the engine asked for the restart, while the heartbeat waits for a beat on its way: the run stops
    // before it tells the server of the restart, and the restart stays due for the next start.
    [Fact]
    public async Task AStopRightAfterTheRestartStepKeepsTheRestartDue()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());
        OnFirstBeatOnceTheRestartIsDue(server, server.Stop.Cancel);

        RunResult result = await RunAsync(server, TestRuns.Run([TestRuns.Partition, TestRuns.Reboot, TestRuns.Apply, TestRuns.Unattend], _image));

        Assert.Equal(new RunResult(RunOutcome.Stopped), result);
        Assert.Equal(["list", "prepare", "partition 0"], _tools.Calls);
        Assert.DoesNotContain(server.RunReports, report => report.Activity == RunActivity.Restarting);
        Assert.Equal(RestartInto.WindowsPE, RestartDue());
    }

    // The run asked for the restart, so the machine restarts all the same, and should that fail, the next start does.
    [Fact]
    public async Task ARefusedTokenOnABeatRightAfterTheRestartStepKeepsTheRestartDue()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());
        OnFirstBeatOnceTheRestartIsDue(server, () => throw new AgentTokenRejectedException());

        RunResult result = await RunAsync(server, TestRuns.Run([TestRuns.Partition, TestRuns.Reboot, TestRuns.Apply, TestRuns.Unattend], _image));

        Assert.Equal(RunOutcome.Restarting, result.Outcome);
        Assert.Equal(["list", "prepare", "partition 0", "reboot into Windows PE"], _tools.Calls);
        Assert.Equal(RestartInto.WindowsPE, RestartDue());
    }

    [Fact]
    public async Task ARefusedTokenOnTheRestartReportKeepsTheRestartDue()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());
        server.AnswerRunReports = (report, token) => report.Activity == RunActivity.Restarting
            ? throw new AgentTokenRejectedException()
            : new AgentRunReportResult(token, "resume", "run-token-1");
        _tools.FailAt = "reboot";

        RunResult result = await RunAsync(server, TestRuns.Run([TestRuns.Partition, TestRuns.Reboot, TestRuns.Apply, TestRuns.Unattend], _image));

        Assert.Equal(RunOutcome.Restarting, result.Outcome);
        Assert.Equal(["list", "prepare", "partition 0", "reboot into Windows PE"], _tools.Calls);
        Assert.Equal(RestartInto.WindowsPE, RestartDue());
    }

    [Fact]
    public async Task ARunWhosePartitionsAreGoneFailsAndForgetsItsState()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        AgentLog log = Log();
        AgentRun run = InstallWindows() with { State = DeploymentState.Running };
        SequenceState state = SequenceStates.Start(run.Id, run.Sequence) with
        {
            NextIndex = 1,
            Steps = [new StepRunState(TestRuns.Partition.Id, StepState.Done, null), .. run.Sequence.Steps.Skip(1).Select(step => new StepRunState(step.Id, StepState.Pending, null))],
            Variables = RunVariables.Of(_tools.Volumes),
        };
        RunFiles files = RunFiles.In(Windows, log);
        await files.SaveStateAsync(state, cancellationToken);
        await files.SaveTokenAsync("run-token-1", cancellationToken);
        _tools.FailAt = "find";
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        RunResult result = await RunAsync(server, run, new() { Resumed = await LocalRun.LoadAsync(Windows, log, cancellationToken), RunToken = "run-token-1" });

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        AgentRunReport report = Assert.Single(server.RunReports);
        Assert.Equal((DeploymentState.Failed, "The scripted step failed."), (report.State, report.Error));
        Assert.Equal([new StepRunState(TestRuns.Partition.Id, StepState.Done, null)], report.Steps);
        Assert.False(File.Exists(StatePath));
        Assert.False(File.Exists(files.TokenPath));
    }
}
