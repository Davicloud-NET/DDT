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

public sealed class SequenceRunnerTests : IDisposable
{
    private static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");
    private static readonly MachineIdentity s_identity = new DryRunMachineIdentityReader(1).Read();

    private static readonly InjectDriversStep s_drivers = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000b005"), Name = "Drivers" };

    private readonly FakeDeploymentTools _tools = new();
    private readonly RecordingToolRunner _toolRunner = new();
    private readonly TestImage _image = new();
    private readonly string _system;
    private DeploymentTokens _tokens = new("session-0", "resume-0");

    public SequenceRunnerTests()
    {
        _system = TestAgents.SystemDirectory(_tools);
    }

    public void Dispose() => _tools.Dispose();

    private string Windows => _tools.Volumes.Windows;

    private string StatePath => RunFiles.StatePathIn(Windows);

    [Fact]
    public async Task RunsTheStepsInOrderAndRestartsIntoWindows()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        RunResult result = await RunAsync(server);

        Assert.Equal(new RunResult(RunOutcome.Finished), result);
        Assert.Equal(["list", "prepare", "partition 0", "apply 1", "bcd", "firmware after the answer file", "reboot"], _tools.Calls);
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

        await RunAsync(server, time: time, log: log);

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

    // What LargeRun needs, in MB: 1340 for the partitions, 4608 for the image's download and 20480 for its files, the
    // package's 1024 twice, and 2048 to spare.
    private const long LargeRunBytes = 30524L * 1024 * 1024;

    public static TheoryData<string> PreflightFailures =>
    [
        "invalid sequence",
        "no image for the step",
        "no DISM",
        "no PowerShell",
        "no disk",
        "several disks",
        "chosen before the agent started",
        "chosen disk gone",
        "another disk under the chosen number",
        "disk too small",
        "wimlib",
        "image missing",
        "image size",
    ];

    [Theory]
    [MemberData(nameof(PreflightFailures))]
    public async Task APreflightFailureReportsFailedWithTheDiskUntouched(string failure)
    {
        AgentRun run = InstallWindows();
        LocalDisk? confirmedDisk = null;
        ScriptedAgentServer server = new ScriptedAgentServer().ServeFile(_image.Sha256, _image.Content);
        string expected;

        switch (failure)
        {
            case "invalid sequence":
                run = TestRuns.Run([TestRuns.Apply, TestRuns.Partition], _image);
                expected = "Install Windows cannot run: ";
                break;
            case "no image for the step":
                run = run with { Images = [] };
                expected = "The server sent no image for step Apply image. Assign the sequence again.";
                break;
            case "no DISM":
                run = TestRuns.Run([TestRuns.Partition, TestRuns.Apply, s_drivers, TestRuns.Unattend], _image);
                File.Delete(InjectDriversStepRunner.DismIn(_system));
                expected = InjectDriversStepRunner.NoDismMessage;
                break;
            case "no PowerShell":
                run = TestRuns.Run(
                    [TestRuns.Partition, TestRuns.Script(1, interpreter: ScriptInterpreter.PowerShell), TestRuns.Apply, TestRuns.Unattend],
                    _image);
                File.Delete(RunScriptStepRunner.PowerShellIn(_system));
                expected = RunScriptStepRunner.NoPowerShellMessage;
                break;
            case "no disk":
                _tools.Disks.Clear();
                expected = SequenceRunner.NoDiskMessage;
                break;
            case "several disks":
                _tools.Disks.Add(FakeDeploymentTools.Disk(1));
                expected = SequenceRunner.SeveralDisksMessage;
                break;
            case "chosen before the agent started":
                run = InstallWindows(diskNumber: 0);
                expected = "Disk 0 was chosen before the agent started again, and disk numbers can change when a machine restarts";
                break;
            case "chosen disk gone":
                run = InstallWindows(diskNumber: 5);
                confirmedDisk = FakeDeploymentTools.Disk(5);
                expected = "Disk 5, chosen at this machine, is not a disk DDT can install on now.";
                break;
            case "another disk under the chosen number":
                run = InstallWindows(diskNumber: 0);
                confirmedDisk = FakeDeploymentTools.Disk(0) with { Model = "USB stick", BusType = StorageBusType.Usb };
                expected = "Disk 0 is no longer the disk chosen at this machine (USB stick, 256 GB)";
                break;
            case "disk too small":
                // A byte short, so leaving out any part, even the reserved partition, would let the run through.
                run = LargeRun();
                _tools.Disks[0] = FakeDeploymentTools.Disk(0, sizeBytes: LargeRunBytes - 1);
                expected = "Disk 0 holds 30 GB, but Install Windows needs 30 GB: 1.3 GB for the boot and recovery partitions, 4.5 GB for the " +
                    "image downloads, 20 GB for the installed files, 2 GB for the packages and their contents and 2 GB to spare. Run it on a larger disk.";
                break;
            case "wimlib":
                _tools.FailAt = "prepare";
                _tools.Failure = new DeploymentStepException("wimlib cannot be used.");
                expected = "wimlib cannot be used.";
                break;
            case "image missing":
                server.OnHeadRunFile(_ => throw new AgentRequestException("404", "The image file is missing on the server.", HttpStatusCode.NotFound));
                expected = "The image file is missing on the server.";
                break;
            default:
                server.OnHeadRunFile(_ => 12);
                expected = "The server's file for Windows 11 Pro holds 12 bytes instead of 3000. Upload the image again.";
                break;
        }

        RunResult result = await RunAsync(server, run, confirmedDisk: confirmedDisk);

        Assert.Equal(new RunResult(RunOutcome.Failed), result);
        AgentRunReport report = Assert.Single(server.RunReports);
        Assert.Equal(DeploymentState.Failed, report.State);
        Assert.StartsWith(expected, report.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(_tools.Calls, call => call.StartsWith("partition", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ErasesADiskThatHoldsExactlyWhatTheRunNeeds()
    {
        _tools.Disks[0] = FakeDeploymentTools.Disk(0, sizeBytes: LargeRunBytes);
        AgentRun run = LargeRun();
        ScriptedAgentServer server = new ScriptedAgentServer().OnHeadRunFile(_ => run.Images[0].SizeBytes);

        await RunAsync(server, run);

        Assert.Contains("partition 0", _tools.Calls);
    }

    private AgentRun LargeRun() => InstallWindows() with
    {
        Images = [_image.RunImage with { SizeBytes = 4608L * 1024 * 1024, InstalledBytes = 20L * 1024 * 1024 * 1024 }],
        Packages = [new AgentRunPackage(s_drivers.Id, "Drivers", _image.Sha256, 1024L * 1024 * 1024)],
    };

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
        SequenceRunner runner = TestAgents.Runner(server, _tools, Log(), new ImmediateTimeProvider(), toolRunner: _toolRunner, systemDirectory: _system, dryRun: false);

        RunResult result = await runner.RunAsync(
            s_machineId,
            TestRuns.Run([TestRuns.Script(1) with { RebootExitCodes = [] }]),
            null,
            null,
            _tokens,
            s_identity,
            server.Stop.Token);

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
        Assert.NotNull(result.UnsentFailure);
        Assert.Equal((DeploymentState.Failed, "The scripted step failed."), (result.UnsentFailure.State, result.UnsentFailure.Error));
        Assert.Contains(new StepRunState(TestRuns.Apply.Id, StepState.Failed, "The scripted step failed."), result.UnsentFailure.Steps);
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

        RunResult result = await RunAsync(server, run, await LocalRun.LoadAsync(Windows, log, cancellationToken), runToken: "run-token-1");

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal((DeploymentState.Failed, SequenceEngine.InterruptedError), (server.RunReports[^1].State, server.RunReports[^1].Error));
        Assert.False(File.Exists(UnattendFile.PathIn(Windows)));
        Assert.False(File.Exists(StatePath));
    }

    [Fact]
    public async Task ARefusedTokenOnAStepsOwnCallEndsTheRunAsOnABeat()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .ServeFile(_image.Sha256, _image.Content)
            .OnRunReport(DeploymentState.Running, _ => new AgentRunReportResult("session-0", "resume-0", "run-token-1"))
            .OnRunUnattend(_ => throw new AgentTokenRejectedException());

        RunResult result = await RunAsync(server);

        // The registration with the run token decides whether the run goes on, so its state stays.
        Assert.Equal(RunOutcome.TokenRejected, result.Outcome);
        Assert.NotNull(result.UnsentFailure);
        Assert.Contains(new StepRunState(TestRuns.Unattend.Id, StepState.Failed, SequenceRunner.LostContactMessage), result.UnsentFailure.Steps);
        Assert.Equal(StepState.Running, (await LoadStateAsync())?.Steps[2].State);
        Assert.Equal("run-token-1", await RunFiles.In(Windows, Log()).LoadTokenAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(server.RunReports, report => report.State != DeploymentState.Running);
    }

    [Fact]
    public async Task ARestartTheRunAskedForHappensEvenWhenTheTokenIsRefused()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ManualTimeProvider time = new();
        TaskCompletionSource scriptRuns = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource refused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _toolRunner.AnswerExitCode = (_, _, _) =>
        {
            scriptRuns.TrySetResult();
            refused.Task.Wait(cancellationToken);

            return 3010;
        };
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());
        AgentRun run = TestRuns.Run([TestRuns.Partition, TestRuns.Script(1), TestRuns.Apply, TestRuns.Unattend], _image);

        // The script blocks the thread it runs on until a beat is refused.
        Task<RunResult> running = Task.Run(() => RunAsync(server, run, time: time, heartbeatInterval: TimeSpan.FromSeconds(10)), cancellationToken);
        await scriptRuns.Task.WaitAsync(cancellationToken);
        server.AnswerRunReports = (_, _) =>
        {
            refused.TrySetResult();

            throw new AgentTokenRejectedException();
        };
        await time.AdvanceUntilAsync(TimeSpan.FromSeconds(10), () => refused.Task.IsCompleted);

        RunResult result = await running.WaitAsync(cancellationToken);

        // Going on without the restart would run the next steps in the same Windows PE; after it, the registration
        // decides whether the run goes on.
        Assert.Equal(RunOutcome.Restarting, result.Outcome);
        Assert.Equal("reboot into Windows PE", _tools.Calls[^1]);
        Assert.Equal(2, (await LoadStateAsync())?.NextIndex);
    }

    [Fact]
    public async Task AStopKeepsTheRunsStateForTheNextStart()
    {
        _tools.ApplyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        Task<RunResult> run = RunAsync(server);
        await _tools.ApplyStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await server.Stop.CancelAsync();

        Assert.Equal(new RunResult(RunOutcome.Stopped), await run);
        Assert.Equal(StepState.Running, (await LoadStateAsync())?.Steps[1].State);
        Assert.DoesNotContain(server.RunReports, report => report.State != DeploymentState.Running);
    }

    [Fact]
    public async Task ARefusedTokenOnABeatEndsTheRunAndKeepsItsState()
    {
        ManualTimeProvider time = new();
        _tools.ApplyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        Task<RunResult> run = RunAsync(server, time: time, heartbeatInterval: TimeSpan.FromSeconds(10));
        await _tools.ApplyStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        server.AnswerRunReports = (_, _) => throw new AgentTokenRejectedException();
        await time.AdvanceUntilAsync(TimeSpan.FromSeconds(10), () => run.IsCompleted);

        RunResult result = await run;

        // The registration with the run token decides whether the run goes on, so its state stays.
        Assert.Equal(RunOutcome.TokenRejected, result.Outcome);
        Assert.NotNull(result.UnsentFailure);
        Assert.Contains(new StepRunState(TestRuns.Apply.Id, StepState.Failed, SequenceRunner.LostContactMessage), result.UnsentFailure.Steps);
        Assert.Equal(StepState.Running, (await LoadStateAsync())?.Steps[1].State);
        Assert.DoesNotContain(server.RunReports, report => report.State != DeploymentState.Running);
        Assert.DoesNotContain("reboot", _tools.Calls);
    }

    [Fact]
    public async Task ARefusedBeatFailsTheRunWithTheServersReason()
    {
        ManualTimeProvider time = new();
        _tools.ApplyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        Task<RunResult> run = RunAsync(server, time: time, heartbeatInterval: TimeSpan.FromSeconds(10));
        await _tools.ApplyStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        server.AnswerRunReports = (report, token) => report.State == DeploymentState.Running
            ? throw new AgentRequestException("409", "This run is no longer running.", HttpStatusCode.Conflict)
            : new AgentRunReportResult(token, "resume", null);
        await time.AdvanceUntilAsync(TimeSpan.FromSeconds(10), () => run.IsCompleted);

        RunResult result = await run;

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        AgentRunReport report = server.RunReports[^1];
        Assert.Equal((DeploymentState.Failed, "This run is no longer running."), (report.State, report.Error));
        Assert.Contains(new StepRunState(TestRuns.Apply.Id, StepState.Failed, "This run is no longer running."), report.Steps);
        Assert.False(File.Exists(StatePath));
    }

    [Fact]
    public async Task AStopAfterWindowsWasPutFirstPutsTheBootOrderBackAndKeepsTheRun()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());
        _tools.PuttingWindowsFirst = server.Stop.Cancel;

        RunResult result = await RunAsync(server);

        // The run goes on after a start from the network, so its state and answer file stay.
        Assert.Equal(new RunResult(RunOutcome.Stopped), result);
        Assert.Equal(["firmware after the answer file", "restore"], _tools.Calls[^2..]);
        Assert.True(File.Exists(UnattendFile.PathIn(Windows)));
        Assert.True(File.Exists(StatePath));
        Assert.DoesNotContain(server.RunReports, report => report.State != DeploymentState.Running);
    }

    [Fact]
    public async Task ARefusedTokenOnABeatAfterWindowsWasPutFirstPutsTheBootOrderBackAndKeepsTheRun()
    {
        ManualTimeProvider time = new();
        _tools.FirmwareGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        Task<RunResult> run = RunAsync(server, time: time, heartbeatInterval: TimeSpan.FromSeconds(10));
        await _tools.FirmwareStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        server.AnswerRunReports = (_, _) => throw new AgentTokenRejectedException();
        await time.AdvanceUntilAsync(TimeSpan.FromSeconds(10), () => run.IsCompleted);

        RunResult result = await run;

        // The registration with the run token decides whether the run goes on, so its state and answer file stay.
        Assert.Equal(RunOutcome.TokenRejected, result.Outcome);
        Assert.Equal(SequenceRunner.LostContactMessage, result.UnsentFailure?.Error);
        Assert.Equal(["firmware after the answer file", "restore"], _tools.Calls[^2..]);
        Assert.True(File.Exists(UnattendFile.PathIn(Windows)));
        Assert.True(File.Exists(StatePath));
        Assert.DoesNotContain(server.RunReports, report => report.State != DeploymentState.Running);
    }

    [Fact]
    public async Task ARefusedLogBeforeTheDoneReportUndoesTheRunAndDoesNotRestart()
    {
        // Refused once the heartbeat has ended: an operator stopped the run just before its end.
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());
        server.AnswerLogs = batch =>
        {
            if (batch.Lines.Any(line => line.Message.StartsWith("The run is done.", StringComparison.Ordinal)))
            {
                throw new AgentTokenRejectedException();
            }
        };

        RunResult result = await RunAsync(server);

        Assert.Equal(RunOutcome.TokenRejected, result.Outcome);
        Assert.Equal(["firmware after the answer file", "restore"], _tools.Calls[^2..]);
        Assert.False(File.Exists(UnattendFile.PathIn(Windows)));
        Assert.False(File.Exists(StatePath));
        Assert.DoesNotContain(server.RunReports, report => report.State != DeploymentState.Running);
    }

    [Fact]
    public async Task BeatsWithTheCurrentStepAndPercentEveryInterval()
    {
        ManualTimeProvider time = new();
        _tools.ApplyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        Task<RunResult> run = RunAsync(server, time: time, heartbeatInterval: TimeSpan.FromSeconds(10));
        await _tools.ApplyStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        // The apply's 50 percent is 70 percent of the step, after the download's 40. A change sends at most one beat,
        // so a second one at the same step and percent came from the interval.
        await time.AdvanceUntilAsync(
            TimeSpan.FromSeconds(10),
            () => server.RunReports.Count(report => report.State == DeploymentState.Running
                && report.CurrentStepId == TestRuns.Apply.Id
                && report.Percent == 70) >= 2);

        _tools.ApplyGate.SetResult();

        Assert.Equal(RunOutcome.Finished, (await run.WaitAsync(TestContext.Current.CancellationToken)).Outcome);
    }

    [Fact]
    public async Task ATransientFailureOfABeatLeavesTheRunGoing()
    {
        ManualTimeProvider time = new();
        _tools.ApplyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        Task<RunResult> run = RunAsync(server, time: time, heartbeatInterval: TimeSpan.FromSeconds(10));
        await _tools.ApplyStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        // A proxy restarting, then a server that took too long: both pass on their own.
        int beats = 0;
        server.AnswerRunReports = (report, token) => report.State != DeploymentState.Running
            ? new AgentRunReportResult(token, "resume", null)
            : Interlocked.Increment(ref beats) switch
            {
                1 => throw new HttpRequestException("502", null, HttpStatusCode.BadGateway),
                2 => throw new AgentRequestException("408", null, HttpStatusCode.RequestTimeout),
                _ => new AgentRunReportResult(token, "resume", null),
            };

        await time.AdvanceUntilAsync(TimeSpan.FromSeconds(10), () => Volatile.Read(ref beats) >= 3);

        _tools.ApplyGate.SetResult();

        Assert.Equal(RunOutcome.Finished, (await run.WaitAsync(TestContext.Current.CancellationToken)).Outcome);
    }

    [Fact]
    public async Task UsesTheTokensEachReportHandsOut()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRunReport(DeploymentState.Running, _ => new AgentRunReportResult("session-1", "resume-1", null));

        await RunAsync(server);

        Assert.Contains($"open-file {_image.Sha256} 0 session-1", server.Calls);
        Assert.Contains($"run-unattend {TestRuns.Unattend.Id} session-1", server.Calls);
        Assert.Equal("session-1", _tokens.Token);
    }

    [Fact]
    public async Task KeepsTheNewestRunTokenOnTheDisk()
    {
        int issued = 0;
        ScriptedAgentServer server = new()
        {
            AnswerRunReports = (report, token) =>
                new AgentRunReportResult(token, "resume", $"run-token-{Interlocked.Increment(ref issued)}"),
        };

        RunResult result = await RunAsync(server, TestRuns.Run([TestRuns.Partition, TestRuns.Reboot]));

        Assert.Equal(RunOutcome.Restarting, result.Outcome);
        Assert.Equal($"run-token-{issued}", _tokens.RunToken);
        Assert.Equal(_tokens.RunToken, await RunFiles.In(Windows, Log()).LoadTokenAsync(TestContext.Current.CancellationToken));
    }

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

        RunResult first = await RunAsync(server, run, time: time, log: log, rebooter: new WindowsPERebooter(_toolRunner, firmware, log));

        Assert.Equal(RunOutcome.Restarting, first.Outcome);
        Assert.Equal(["list", "prepare", "partition 0"], _tools.Calls);
        Assert.Equal(["BootNext"], firmware.Writes);
        Assert.EndsWith("wpeutil.exe reboot", _toolRunner.Calls[^1], StringComparison.Ordinal);
        Assert.Equal(RunActivity.Restarting, server.RunReports[^1].Activity);

        // The next start of Windows PE: only the disk remembers the run.
        LocalRun? found = await LocalRun.LoadAsync(Windows, log, cancellationToken);
        Assert.NotNull(found);
        Assert.Equal((3, "run-token-1"), (found.State.NextIndex, found.RunToken));
        int reportsBefore = server.RunReports.Count;

        RunResult second = await RunAsync(server, run with { State = DeploymentState.Running }, found, runToken: found.RunToken);

        Assert.Equal(RunOutcome.Finished, second.Outcome);
        Assert.Equal(
            ["list", "prepare", "partition 0", $"find {FakeDeploymentTools.WindowsPartitionId}", "apply 1", "bcd", "firmware after the answer file", "reboot"],
            _tools.Calls);
        Assert.Single(_toolRunner.Calls, call => call.EndsWith("c001.cmd", StringComparison.Ordinal));
        Assert.Equal([StepState.Done, StepState.Done, StepState.Done], server.RunReports[reportsBefore].Steps.Take(3).Select(step => step.State));
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

        RunResult result = await RunAsync(server, run, await LocalRun.LoadAsync(Windows, log, cancellationToken), runToken: "run-token-1");

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        AgentRunReport report = Assert.Single(server.RunReports);
        Assert.Equal((DeploymentState.Failed, "The scripted step failed."), (report.State, report.Error));
        Assert.Equal([new StepRunState(TestRuns.Partition.Id, StepState.Done, null)], report.Steps);
        Assert.False(File.Exists(StatePath));
        Assert.False(File.Exists(files.TokenPath));
    }

    [Fact]
    public async Task ARunThatGoesOnInWindowsFailsWithoutAHandOver()
    {
        RunScriptStep windows = TestRuns.Script(2, SequencePhase.Windows);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        RunResult result = await RunAsync(server, TestRuns.Run([.. TestRuns.InstallWindows, windows], _image));

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.EndsWith("cannot hand it over to Windows.", server.RunReports[^1].Error, StringComparison.Ordinal);
    }

    private AgentRun InstallWindows(int? diskNumber = null) => TestRuns.Run(TestRuns.InstallWindows, _image, diskNumber: diskNumber);

    private static AgentLog Log() => new(new ImmediateTimeProvider(), TextWriter.Null);

    private Task<SequenceState?> LoadStateAsync() => RunFiles.In(Windows, Log()).LoadStateAsync(TestContext.Current.CancellationToken);

    private Task<RunResult> RunAsync(
        ScriptedAgentServer server,
        AgentRun? run = null,
        LocalRun? resumed = null,
        LocalDisk? confirmedDisk = null,
        TimeProvider? time = null,
        TimeSpan? heartbeatInterval = null,
        AgentLog? log = null,
        IRebooter? rebooter = null,
        string? runToken = null)
    {
        time ??= new ImmediateTimeProvider();
        _tokens = new DeploymentTokens("session-0", "resume-0", runToken);
        SequenceRunner runner = TestAgents.Runner(
            server,
            _tools,
            log ?? new AgentLog(time, TextWriter.Null),
            time,
            heartbeatInterval,
            _toolRunner,
            rebooter: rebooter,
            systemDirectory: _system);

        return runner.RunAsync(s_machineId, run ?? InstallWindows(), resumed, confirmedDisk, _tokens, s_identity, server.Stop.Token);
    }
}
