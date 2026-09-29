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

// A fresh run from its first step to its end: the order of its steps, reports and restarts, and how a failed step ends it.
public sealed class SequenceRunnerTests : SequenceRunnerTestBase
{
    [Fact]
    public async Task RunsTheStepsInOrderAndRestartsIntoWindows()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        RunResult result = await RunAsync(server);

        Assert.Equal(new RunResult(RunOutcome.Finished), result);
        Assert.Equal(["list", "prepare", "partition 0", "apply 1", "bcd", "firmware after the answer file", "reboot"], _tools.Calls);
        Assert.Equal(RestartInto.Windows, RestartDue());
        Assert.True(_tools.ImageWasThereToApply);
        Assert.False(Directory.Exists(Path.Combine(Windows, "DDT")));
        Assert.Equal([$"head-file {_image.Sha256} session-0", "run-report Running session-0"], server.Calls.Take(2));

        List<AgentRunReport> reports = server.RunReports;
        Assert.Equal((DeploymentState.Running, RunActivity.Preparing), (reports[0].State, reports[0].Activity));
        Assert.Empty(reports[0].Steps);
        Assert.All(reports[..^1], report => Assert.Equal(DeploymentState.Running, report.State));
        Assert.Equal((DeploymentState.Done, RunActivity.Finishing), (reports[^1].State, reports[^1].Activity));
        Assert.Equal([StepState.Done, StepState.Done, StepState.Done], reports[^1].Steps.Select(step => step.State));

        // A step that has left Pending never goes back.
        Assert.Equal(reports.Select(report => report.Steps.Count).Order(), reports.Select(report => report.Steps.Count));

        // The server hands out the answer file only for the step it has seen running.
        List<string> calls = server.Calls;
        int fetch = calls.IndexOf($"run-unattend {TestRuns.Unattend.Id} session-0");
        int reportsBefore = calls.Take(fetch).Count(call => call.StartsWith("run-report", StringComparison.Ordinal));
        Assert.Contains(new StepRunState(TestRuns.Unattend.Id, StepState.Running, null), reports[reportsBefore - 1].Steps);

        // The machine log is readable by every viewer, so the answer file's password never reaches it.
        Assert.Contains(server.SentLines, line => line.Message.StartsWith("Wrote the answer file: ", StringComparison.Ordinal));
        Assert.DoesNotContain(server.SentLines, line => line.Message.Contains("c2VjcmV0UGFzc3dvcmQ=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1, "1 step")]
    [InlineData(2, "2 steps")]
    public async Task SaysHowManyStepsTheRunHas(int count, string steps)
    {
        StringWriter console = new();
        ImmediateTimeProvider time = new();

        // Before a partition, Windows PE cannot restart, so the scripts cannot ask for it.
        AgentRun run = TestRuns.Run([.. Enumerable.Range(1, count).Select(number => TestRuns.Script(number) with { RebootExitCodes = [] })]);

        RunResult result = await RunAsync(new ScriptedAgentServer(), run, new() { Time = time, Log = new AgentLog(time, console) });

        Assert.Equal(RunOutcome.Finished, result.Outcome);
        Assert.Contains($"The run of Install Windows begins: {steps}.", console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EndsWithTheLogThenTheDoneReportThenOneRestart()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        await RunAsync(server);

        List<string> calls = server.Calls;
        Assert.Equal("run-report Done session-0", calls[^1]);
        Assert.StartsWith("log ", calls[^2], StringComparison.Ordinal);
        Assert.Single(_tools.Calls, call => call == "reboot");
    }

    [Fact]
    public async Task SendsTheWholeLogBeforeTheDoneReport()
    {
        // More lines than the beats after the apply can carry, so only the end's flushes can deliver them all.
        const int appliedLines = 1500;
        ImmediateTimeProvider time = new();
        AgentLog log = new(time, TextWriter.Null);
        _tools.Applied = _ =>
        {
            for (int line = 1; line <= appliedLines; line++)
            {
                log.Information($"Applied file {line}.");
            }
        };
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        await RunAsync(server, options: new() { Time = time, Log = log });

        Assert.Equal("run-report Done session-0", server.Calls[^1]);
        Assert.Contains(server.SentLines, line => line.Message == $"Applied file {appliedLines}.");
        Assert.Contains(server.SentLines, line => line.Message.StartsWith("The run is done.", StringComparison.Ordinal));
        Assert.Equal(0, log.QueuedLines);
    }

    [Fact]
    public async Task RestartsOnceEvenWhenTheDoneReportIsRefused()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRunReport(DeploymentState.Done, _ => throw new AgentTokenRejectedException());

        RunResult result = await RunAsync(server);

        Assert.Equal(RunOutcome.Finished, result.Outcome);
        Assert.Equal("run-report Done session-0", server.Calls[^1]);
        Assert.Single(_tools.Calls, call => call == "reboot");
    }

    [Fact]
    public async Task RestartsOnceWhenTheDoneReportNeverGetsThrough()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        for (int attempt = 0; attempt <= ServerCallRules.MaxRetries; attempt++)
        {
            server.OnRunReport(DeploymentState.Done, _ => throw new HttpRequestException("503", null, HttpStatusCode.ServiceUnavailable));
        }

        RunResult result = await RunAsync(server);

        Assert.Equal(RunOutcome.Finished, result.Outcome);
        Assert.Equal(ServerCallRules.MaxRetries + 1, server.Calls.Count(call => call.StartsWith("run-report Done", StringComparison.Ordinal)));
        Assert.Single(_tools.Calls, call => call == "reboot");
    }

    [Fact]
    public async Task RunsAPowerShellStepInWindowsWithABootImageWithoutPowerShell()
    {
        File.Delete(RunScriptStepRunner.PowerShellIn(_system));
        AgentRun run = TestRuns.Run([.. TestRuns.InstallWindows, TestRuns.Script(4, SequencePhase.Windows, ScriptInterpreter.PowerShell)], _image);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        await RunAsync(server, run);

        // The installed Windows brings its own PowerShell, so the lean boot image is enough.
        Assert.Contains("partition 0", _tools.Calls);
        Assert.DoesNotContain(server.RunReports, report => report.Error == RunScriptStepRunner.NoPowerShellMessage);
    }

    [Fact]
    public async Task LeavesTheDisksAloneForASequenceThatErasesNone()
    {
        ScriptedAgentServer server = new();

        // Before a partitioning a script has nowhere to keep the run for a restart.
        RunResult result = await RunAsync(server, TestRuns.Run([TestRuns.Script(1) with { RebootExitCodes = [] }]));

        Assert.Equal(RunOutcome.Finished, result.Outcome);
        Assert.Equal(["reboot"], _tools.Calls);
        Assert.Single(_toolRunner.Calls, call => call.EndsWith("c001.cmd", StringComparison.Ordinal));
        Assert.Equal(DeploymentState.Done, server.RunReports[^1].State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task OutsideADryRunTheAgentsOwnDirectoryStays(int exitCode)
    {
        // The work directory is X:\DDT in Windows PE, where the agent itself is.
        _toolRunner.AnswerExitCode = (_, _, _) => exitCode;
        ScriptedAgentServer server = new();
        SequenceRunner runner = TestAgents.Runner(server, _tools, Log(), new ImmediateTimeProvider(), new() { ToolRunner = _toolRunner, SystemDirectory = _system, DryRun = false });

        RunResult result = await runner.RunAsync(new RunRequest(s_machineId, TestRuns.Run([TestRuns.Script(1) with { RebootExitCodes = [] }]), null, null, _tokens, s_identity), server.Stop.Token);

        Assert.Equal(exitCode == 0 ? RunOutcome.Finished : RunOutcome.Failed, result.Outcome);
        Assert.True(File.Exists(Path.Combine(_tools.Root, "X", "scripts", $"{TestRuns.Script(1).Id:D}.cmd")));
    }

    public static TheoryData<string> StepFailures => ["partition", "download", "apply", "unattend", "bcd", "firmware"];

    [Theory]
    [MemberData(nameof(StepFailures))]
    public async Task AFailureIsReportedAfterTheLog(string failAt)
    {
        ScriptedAgentServer server = new ScriptedAgentServer().ServeFile(_image.Sha256, _image.Content);

        switch (failAt)
        {
            case "download":
                server.OnOpenRunFile(_ => throw new AgentRequestException("403", "The scripted step failed.", HttpStatusCode.Forbidden));
                break;
            case "unattend":
                server.OnRunUnattend(_ => throw new AgentRequestException("409", "The scripted step failed.", HttpStatusCode.Conflict));
                break;
            default:
                server.OnRunUnattend(_ => TestImage.Unattend);
                _tools.FailAt = failAt;
                break;
        }

        RunResult result = await RunAsync(server);

        Assert.Equal(new RunResult(RunOutcome.Failed), result);
        AgentRunReport report = server.RunReports[^1];
        Assert.Equal((DeploymentState.Failed, "The scripted step failed."), (report.State, report.Error));
        Assert.StartsWith("run-report Failed", server.Calls[^1], StringComparison.Ordinal);
        Assert.StartsWith("log ", server.Calls[^2], StringComparison.Ordinal);
        Assert.DoesNotContain("reboot", _tools.Calls);

        Guid? failedStep = failAt switch
        {
            "partition" => TestRuns.Partition.Id,
            "download" or "apply" => TestRuns.Apply.Id,
            "unattend" => TestRuns.Unattend.Id,
            _ => null,
        };

        StepRunState[] failed = failedStep is { } id ? [new StepRunState(id, StepState.Failed, "The scripted step failed.")] : [];
        Assert.Equal(failed, report.Steps.Where(step => step.State == StepState.Failed));
    }

    [Fact]
    public async Task AFailureAfterWindowsWasPutFirstUndoesItAndForgetsTheRun()
    {
        _tools.FailAt = "firmware";

        RunResult result = await RunAsync(_image.Serve(new ScriptedAgentServer()));

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal(["firmware after the answer file", "restore"], _tools.Calls[^2..]);
        Assert.False(File.Exists(UnattendFile.PathIn(Windows)));
        Assert.False(File.Exists(StatePath));
        Assert.False(File.Exists(RunFiles.In(Windows, Log()).TokenPath));
    }

    [Fact]
    public async Task AFailureBeforeWindowsWasPutFirstLeavesTheBootOrderAlone()
    {
        _tools.FailAt = "bcd";

        RunResult result = await RunAsync(_image.Serve(new ScriptedAgentServer()));

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.DoesNotContain("restore", _tools.Calls);
        Assert.False(File.Exists(UnattendFile.PathIn(Windows)));
        Assert.False(File.Exists(StatePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFailureTheServerWasNotToldAboutIsHandedBack(bool tokenRefused)
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        if (tokenRefused)
        {
            server.OnRunReport(DeploymentState.Failed, _ => throw new AgentTokenRejectedException());
        }
        else
        {
            for (int attempt = 0; attempt <= ServerCallRules.MaxRetries; attempt++)
            {
                server.OnRunReport(DeploymentState.Failed, _ => throw new HttpRequestException("503", null, HttpStatusCode.ServiceUnavailable));
            }
        }

        _tools.FailAt = "apply";

        RunResult result = await RunAsync(server);

        Assert.Equal(tokenRefused ? RunOutcome.TokenRejected : RunOutcome.Failed, result.Outcome);
        Assert.NotNull(result.UnsentReport);
        Assert.Equal((DeploymentState.Failed, "The scripted step failed."), (result.UnsentReport.State, result.UnsentReport.Error));
        Assert.Contains(new StepRunState(TestRuns.Apply.Id, StepState.Failed, "The scripted step failed."), result.UnsentReport.Steps);
    }

    [Fact]
    public async Task ARunWhoseAnswerFileStepWasInterruptedDeletesTheFileWhenItFails()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        AgentLog log = Log();
        AgentRun run = InstallWindows() with { State = DeploymentState.Running };
        SequenceState state = SequenceStates.Start(run.Id, run.Sequence) with
        {
            NextIndex = 2,
            Steps =
            [
                new StepRunState(TestRuns.Partition.Id, StepState.Done, null),
                new StepRunState(TestRuns.Apply.Id, StepState.Done, null),
                new StepRunState(TestRuns.Unattend.Id, StepState.Running, null),
            ],
            Variables = RunVariables.Of(_tools.Volumes),
        };
        RunFiles files = RunFiles.In(Windows, log);
        await files.SaveStateAsync(state, cancellationToken);
        await files.SaveTokenAsync("run-token-1", cancellationToken);
        await UnattendFile.WriteAsync(Windows, TestImage.Unattend, cancellationToken);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        RunResult result = await RunAsync(server, run, new() { Resumed = await LocalRun.LoadAsync(Windows, log, cancellationToken), RunToken = "run-token-1" });

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal((DeploymentState.Failed, SequenceEngine.InterruptedError), (server.RunReports[^1].State, server.RunReports[^1].Error));
        Assert.False(File.Exists(UnattendFile.PathIn(Windows)));
        Assert.False(File.Exists(StatePath));
    }
}
