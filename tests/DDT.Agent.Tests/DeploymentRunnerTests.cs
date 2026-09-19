using System.Net;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class DeploymentRunnerTests : IDisposable
{
    private static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");

    private readonly FakeDeploymentTools _tools = new();
    private readonly TestImage _image = new();

    public void Dispose() => _tools.Dispose();

    private string Windows => Path.Combine(_tools.Root, "W");

    [Fact]
    public async Task RunsTheStepsInOrderAndRestarts()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        DeploymentRunResult result = await RunAsync(server);

        Assert.Equal(DeploymentOutcome.Deployed, result.Outcome);
        Assert.Equal(["list", "prepare", "partition 0", "apply 1", "bcd", "firmware after the answer file", "reboot"], _tools.Calls);
        Assert.True(_tools.ImageWasThereToApply);
        Assert.False(Directory.Exists(Path.Combine(Windows, "DDT")));
        Assert.Equal(["head session-0", "report Running session-0"], server.Calls.Take(2));

        List<AgentDeploymentReport> reports = server.Reports;
        Assert.Equal(new AgentDeploymentReport(DeploymentState.Running, DeploymentStep.Partition, 0, null), reports[0]);
        Assert.Equal(new AgentDeploymentReport(DeploymentState.Done, DeploymentStep.Reboot, 100, null), reports[^1]);
        Assert.All(reports[..^1], report => Assert.Equal(DeploymentState.Running, report.State));

        // A step never goes backwards.
        Assert.Equal(reports.Select(report => report.Step).Order(), reports.Select(report => report.Step));

        // The machine log is readable by every viewer, so the answer file's password never reaches it.
        Assert.Contains(server.SentLines, line => line.Message.StartsWith("Wrote the unattend file: ", StringComparison.Ordinal));
        Assert.DoesNotContain(server.SentLines, line => line.Message.Contains("c2VjcmV0UGFzc3dvcmQ=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WritesTheAnswerFileAndHasSetupDeleteItFirst()
    {
        string scripts = Path.Combine(Windows, "Windows", "Setup", "Scripts");
        _tools.Applied = _ =>
        {
            Directory.CreateDirectory(scripts);
            File.WriteAllText(Path.Combine(scripts, "SetupComplete.cmd"), "echo from the image\r\nexit /b 0\r\n");
        };

        await RunAsync(_image.Serve(new ScriptedAgentServer()));

        Assert.Equal(TestImage.Unattend, await File.ReadAllTextAsync(UnattendFile.PathIn(Windows), TestContext.Current.CancellationToken));
        Assert.Equal(
            $"{UnattendFile.CleanupLine}\r\necho from the image\r\nexit /b 0\r\n",
            await File.ReadAllTextAsync(Path.Combine(scripts, "SetupComplete.cmd"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EndsWithTheLogThenTheDoneReportThenOneRestart()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        await RunAsync(server);

        List<string> calls = server.Calls;
        Assert.Equal("report Done session-0", calls[^1]);
        Assert.StartsWith("log ", calls[^2], StringComparison.Ordinal);
        Assert.Equal("reboot", _tools.Calls[^1]);
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

        Assert.Equal("report Done session-0", server.Calls[^1]);
        Assert.Contains(server.SentLines, line => line.Message == $"Applied file {appliedLines}.");
        Assert.Contains(server.SentLines, line => line.Message.StartsWith("Windows is installed.", StringComparison.Ordinal));
        Assert.Equal(0, log.QueuedLines);
    }

    [Fact]
    public async Task RestartsOnceEvenWhenTheDoneReportIsRefused()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnReport(DeploymentState.Done, _ => throw new AgentTokenRejectedException());

        DeploymentRunResult result = await RunAsync(server);

        Assert.Equal(DeploymentOutcome.Deployed, result.Outcome);
        Assert.Equal("report Done session-0", server.Calls[^1]);
        Assert.Single(_tools.Calls, call => call == "reboot");
    }

    [Fact]
    public async Task RestartsOnceWhenTheDoneReportNeverGetsThrough()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        for (int attempt = 0; attempt <= ServerCallRules.MaxRetries; attempt++)
        {
            server.OnReport(DeploymentState.Done, _ => throw new HttpRequestException("503", null, HttpStatusCode.ServiceUnavailable));
        }

        DeploymentRunResult result = await RunAsync(server);

        Assert.Equal(DeploymentOutcome.Deployed, result.Outcome);
        Assert.Equal(ServerCallRules.MaxRetries + 1, server.Calls.Count(call => call.StartsWith("report Done", StringComparison.Ordinal)));
        Assert.Single(_tools.Calls, call => call == "reboot");
    }

    public static TheoryData<string> PreflightFailures =>
    [
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
        AgentDeployment deployment = _image.Deployment();
        LocalDisk? confirmedDisk = null;
        ScriptedAgentServer server = new ScriptedAgentServer().OnHeadImage(() => _image.Content.Length);
        string expected;

        switch (failure)
        {
            case "no disk":
                _tools.Disks.Clear();
                expected = DeploymentRunner.NoDiskMessage;
                break;
            case "several disks":
                _tools.Disks.Add(FakeDeploymentTools.Disk(1));
                expected = DeploymentRunner.SeveralDisksMessage;
                break;
            case "chosen before the agent started":
                deployment = _image.Deployment(diskNumber: 0);
                expected = "Disk 0 was chosen before the agent started again, and disk numbers can change when a machine restarts";
                break;
            case "chosen disk gone":
                deployment = _image.Deployment(diskNumber: 5);
                confirmedDisk = FakeDeploymentTools.Disk(5);
                expected = "Disk 5, chosen at this machine, is not a disk DDT can install on now.";
                break;
            case "another disk under the chosen number":
                deployment = _image.Deployment(diskNumber: 0);
                confirmedDisk = FakeDeploymentTools.Disk(0) with { Model = "USB stick", BusType = StorageBusType.Usb };
                expected = "Disk 0 is no longer the disk chosen at this machine (USB stick, 256 GB)";
                break;
            case "disk too small":
                // Exactly 28 GB, so leaving out any of the four parts would let the image through.
                deployment = _image.Deployment() with { SizeBytes = 4608L * 1024 * 1024, InstalledBytes = 20L * 1024 * 1024 * 1024 };
                _tools.Disks[0] = FakeDeploymentTools.Disk(0, sizeBytes: 25L * 1024 * 1024 * 1024);
                expected = "Disk 0 holds 25 GB, but Windows 11 Pro needs 28 GB: 1.5 GB for the boot and recovery partitions, 4.5 GB for the " +
                    "download, 20 GB for the installed files and 2 GB to spare.";
                break;
            case "wimlib":
                _tools.FailAt = "prepare";
                _tools.Failure = new DeploymentStepException("wimlib cannot be used.");
                expected = "wimlib cannot be used.";
                break;
            case "image missing":
                server = new ScriptedAgentServer()
                    .OnHeadImage(() => throw new AgentRequestException("404", "The image file is missing on the server.", HttpStatusCode.NotFound));
                expected = "The image file is missing on the server.";
                break;
            default:
                server = new ScriptedAgentServer().OnHeadImage(() => 12);
                expected = "The server's file for Windows 11 Pro holds 12 bytes instead of 3000.";
                break;
        }

        DeploymentRunResult result = await RunAsync(server, deployment, confirmedDisk);

        Assert.Equal(DeploymentOutcome.Failed, result.Outcome);
        Assert.Null(result.UnsentError);
        AgentDeploymentReport report = Assert.Single(server.Reports);
        Assert.Equal(DeploymentState.Failed, report.State);
        Assert.Equal(DeploymentStep.Partition, report.Step);
        Assert.StartsWith(expected, report.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(_tools.Calls, call => call.StartsWith("partition", StringComparison.Ordinal));
    }

    public static TheoryData<string, DeploymentStep> StepFailures => new()
    {
        { "partition", DeploymentStep.Partition },
        { "download", DeploymentStep.Download },
        { "apply", DeploymentStep.Apply },
        { "bcd", DeploymentStep.Boot },
        { "unattend", DeploymentStep.Unattend },
        { "firmware", DeploymentStep.Unattend },
    };

    [Theory]
    [MemberData(nameof(StepFailures))]
    public async Task AFailedStepIsReportedWithItsStepAfterTheLog(string failAt, DeploymentStep step)
    {
        ScriptedAgentServer server = new ScriptedAgentServer().OnHeadImage(() => _image.Content.Length);

        server = failAt switch
        {
            "download" => server.OnOpenImage(_ => throw new AgentRequestException("403", "The scripted step failed.", HttpStatusCode.Forbidden)),
            "unattend" => server.OnOpenImage(_image.From)
                .OnUnattend(() => throw new AgentRequestException("409", "The scripted step failed.", HttpStatusCode.Conflict)),
            _ => server.OnOpenImage(_image.From).OnUnattend(() => TestImage.Unattend),
        };

        _tools.FailAt = failAt;

        DeploymentRunResult result = await RunAsync(server);

        Assert.Equal(DeploymentOutcome.Failed, result.Outcome);
        Assert.Null(result.UnsentError);
        Assert.Equal(new AgentDeploymentReport(DeploymentState.Failed, step, server.Reports[^1].Percent, "The scripted step failed."), server.Reports[^1]);
        Assert.StartsWith("report Failed", server.Calls[^1], StringComparison.Ordinal);
        Assert.StartsWith("log ", server.Calls[^2], StringComparison.Ordinal);
        Assert.DoesNotContain("reboot", _tools.Calls);
    }

    [Fact]
    public async Task RunsOnTheChosenDiskWhenItWasConfirmedHere()
    {
        _tools.Disks.Add(FakeDeploymentTools.Disk(1, partitions: 2));

        // The partitions may differ from what was shown: the disk is the same.
        DeploymentRunResult result = await RunAsync(
            _image.Serve(new ScriptedAgentServer()),
            _image.Deployment(diskNumber: 1),
            FakeDeploymentTools.Disk(1, partitions: 4));

        Assert.Equal(DeploymentOutcome.Deployed, result.Outcome);
        Assert.Contains("partition 1", _tools.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFailureTheServerWasNotToldAboutIsHandedBack(bool tokenRefused)
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        if (tokenRefused)
        {
            server.OnReport(DeploymentState.Failed, _ => throw new AgentTokenRejectedException());
        }
        else
        {
            for (int attempt = 0; attempt <= ServerCallRules.MaxRetries; attempt++)
            {
                server.OnReport(DeploymentState.Failed, _ => throw new HttpRequestException("503", null, HttpStatusCode.ServiceUnavailable));
            }
        }

        _tools.FailAt = "bcd";

        DeploymentRunResult result = await RunAsync(server);

        Assert.Equal(tokenRefused ? DeploymentOutcome.TokenRejected : DeploymentOutcome.Failed, result.Outcome);
        Assert.Equal(DeploymentStep.Boot, result.Step);
        Assert.Equal("The scripted step failed.", result.UnsentError);
    }

    [Fact]
    public async Task AFailedAnswerFileLeavesNoPasswordsOnTheDisk()
    {
        // A directory where SetupComplete.cmd belongs makes writing the cleanup line fail.
        _tools.Applied = root => Directory.CreateDirectory(Path.Combine(root, "Windows", "Setup", "Scripts", "SetupComplete.cmd"));

        DeploymentRunResult result = await RunAsync(_image.Serve(new ScriptedAgentServer()));

        Assert.Equal(DeploymentOutcome.Failed, result.Outcome);
        Assert.Equal(DeploymentStep.Unattend, result.Step);
        Assert.False(File.Exists(UnattendFile.PathIn(Windows)));
    }

    [Fact]
    public async Task AFailureAfterTheAnswerFileWasWrittenDeletesItAndPutsTheBootOrderBack()
    {
        _tools.FailAt = "firmware";

        DeploymentRunResult result = await RunAsync(_image.Serve(new ScriptedAgentServer()));

        Assert.Equal(DeploymentOutcome.Failed, result.Outcome);
        Assert.Equal(["firmware after the answer file", "restore"], _tools.Calls[^2..]);
        Assert.False(File.Exists(UnattendFile.PathIn(Windows)));
    }

    [Fact]
    public async Task AFailureBeforeTheAnswerFileLeavesTheBootOrderAlone()
    {
        _tools.FailAt = "bcd";

        DeploymentRunResult result = await RunAsync(_image.Serve(new ScriptedAgentServer()));

        Assert.Equal(DeploymentOutcome.Failed, result.Outcome);
        Assert.DoesNotContain("restore", _tools.Calls);
    }

    [Fact]
    public async Task PutsWindowsFirstAfterTheAnswerFileAndGoesOnWhenThatFails()
    {
        ImmediateTimeProvider time = new();
        AgentLog log = new(time, TextWriter.Null);
        FakeUefiVariables variables = new();
        RecordingToolRunner tools = new();
        bool answerFileWhenListed = false;
        tools.Answer = (fileName, _) =>
        {
            if (Path.GetFileName(fileName) == "bcdedit.exe")
            {
                answerFileWhenListed = File.Exists(UnattendFile.PathIn(Windows));

                throw new DeploymentStepException("bcdedit.exe failed with exit code 0x00000001. Its output is in the machine log.");
            }

            return [];
        };
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        // The system volume is a directory here, so the partition behind it cannot be read and nothing is written.
        DeploymentRunResult result = await RunAsync(server, time: time, log: log, bcdWriter: new BcdbootWriter(tools, variables, log));

        Assert.Equal(DeploymentOutcome.Deployed, result.Outcome);
        Assert.True(answerFileWhenListed);
        Assert.True(File.Exists(UnattendFile.PathIn(Windows)));
        Assert.Empty(variables.Writes);
        Assert.Contains(
            server.SentLines,
            line => line.Level == AgentLogLevel.Warning && line.Message.Contains("so this machine may start from the network again.", StringComparison.Ordinal));
        Assert.Equal("reboot", _tools.Calls[^1]);
    }

    [Fact]
    public async Task AStopJustBeforeTheDoneReportDeletesTheAnswerFileAndDoesNotRestart()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        // The operator's Stop lands once the answer file is written: from then on every call is refused.
        _tools.PuttingWindowsFirst = () =>
        {
            server.AnswerReports = (_, _) => throw new AgentTokenRejectedException();
            server.AnswerLogs = _ => throw new AgentTokenRejectedException();
        };

        DeploymentRunResult result = await RunAsync(server);

        Assert.Equal(DeploymentOutcome.TokenRejected, result.Outcome);
        Assert.Equal(["firmware after the answer file", "restore"], _tools.Calls[^2..]);
        Assert.False(File.Exists(UnattendFile.PathIn(Windows)));
        Assert.DoesNotContain(server.Reports, report => report.State != DeploymentState.Running);
    }

    [Fact]
    public async Task AStopRightAfterTheAnswerFileDeletesItAndPutsTheBootOrderBack()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());
        _tools.PuttingWindowsFirst = server.Stop.Cancel;

        DeploymentRunResult result = await RunAsync(server);

        Assert.Equal(DeploymentOutcome.Stopped, result.Outcome);
        Assert.Equal(["firmware after the answer file", "restore"], _tools.Calls[^2..]);
        Assert.False(File.Exists(UnattendFile.PathIn(Windows)));
        Assert.DoesNotContain(server.Reports, report => report.State != DeploymentState.Running);
    }

    [Fact]
    public async Task ARefusedTokenOnABeatAfterTheAnswerFileDeletesItAndPutsTheBootOrderBack()
    {
        ManualTimeProvider time = new();
        _tools.FirmwareGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        Task<DeploymentRunResult> run = RunAsync(server, time: time, heartbeatInterval: TimeSpan.FromSeconds(10));
        await _tools.FirmwareStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(File.Exists(UnattendFile.PathIn(Windows)));
        server.AnswerReports = (_, _) => throw new AgentTokenRejectedException();
        await time.AdvanceUntilAsync(TimeSpan.FromSeconds(10), () => run.IsCompleted);

        DeploymentRunResult result = await run;

        Assert.Equal(DeploymentOutcome.TokenRejected, result.Outcome);
        Assert.Equal(["firmware after the answer file", "restore"], _tools.Calls[^2..]);
        Assert.False(File.Exists(UnattendFile.PathIn(Windows)));
        Assert.DoesNotContain(server.Reports, report => report.State != DeploymentState.Running);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeletesTheScratchDirectoryWhenTheRunEnds(bool fails)
    {
        _tools.FailAt = fails ? "apply" : null;

        DeploymentRunResult result = await RunAsync(_image.Serve(new ScriptedAgentServer()), scratchDirectory: _tools.Root);

        Assert.Equal(fails ? DeploymentOutcome.Failed : DeploymentOutcome.Deployed, result.Outcome);
        Assert.False(Directory.Exists(_tools.Root));
    }

    [Fact]
    public async Task ARefusedTokenEndsTheRunWithoutAReport()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnHeadImage(() => _image.Content.Length)
            .OnOpenImage(_image.From)
            .OnUnattend(() => throw new AgentTokenRejectedException());

        DeploymentRunResult result = await RunAsync(server);

        Assert.Equal(DeploymentOutcome.TokenRejected, result.Outcome);
        Assert.Equal(DeploymentStep.Unattend, result.Step);
        Assert.DoesNotContain(server.Reports, report => report.State != DeploymentState.Running);
        Assert.DoesNotContain("reboot", _tools.Calls);
    }

    [Fact]
    public async Task AConflictOnTheFirstReportLeavesTheDiskAlone()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnHeadImage(() => _image.Content.Length)
            .OnReport(DeploymentState.Running, _ => throw new AgentRequestException("409", "The deployment was cancelled.", HttpStatusCode.Conflict));

        DeploymentRunResult result = await RunAsync(server);

        Assert.Equal(DeploymentOutcome.Failed, result.Outcome);
        Assert.Equal(["head session-0", "report Running session-0"], server.Calls);
        Assert.DoesNotContain(_tools.Calls, call => call.StartsWith("partition", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UsesTheTokensEachReportHandsOut()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnReport(DeploymentState.Running, _ => new AgentDeploymentReportResult("session-1", "resume-1"));

        DeploymentRunResult result = await RunAsync(server);

        Assert.Contains("open 0 session-1", server.Calls);
        Assert.Contains("unattend session-1", server.Calls);
        Assert.Equal("session-1", result.Token);
    }

    [Fact]
    public async Task BeatsWithTheCurrentStepAndPercentEveryInterval()
    {
        ManualTimeProvider time = new();
        _tools.ApplyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        Task<DeploymentRunResult> run = RunAsync(server, time: time, heartbeatInterval: TimeSpan.FromSeconds(10));
        await _tools.ApplyStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        // A step change sends at most one beat, so a second one at the same step and percent came from the interval.
        await time.AdvanceUntilAsync(
            TimeSpan.FromSeconds(10),
            () => server.Reports.Count(report => report is { State: DeploymentState.Running, Step: DeploymentStep.Apply, Percent: 50 }) >= 2);

        _tools.ApplyGate.SetResult();

        Assert.Equal(DeploymentOutcome.Deployed, (await run.WaitAsync(TestContext.Current.CancellationToken)).Outcome);
    }

    [Fact]
    public async Task ATransientFailureOfABeatLeavesTheRunGoing()
    {
        ManualTimeProvider time = new();
        _tools.ApplyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        Task<DeploymentRunResult> run = RunAsync(server, time: time, heartbeatInterval: TimeSpan.FromSeconds(10));
        await _tools.ApplyStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        // A proxy restarting, then a server that took too long: both pass on their own.
        int beats = 0;
        server.AnswerReports = (report, token) => report.State != DeploymentState.Running
            ? new AgentDeploymentReportResult(token, "resume")
            : Interlocked.Increment(ref beats) switch
            {
                1 => throw new HttpRequestException("502", null, HttpStatusCode.BadGateway),
                2 => throw new AgentRequestException("408", null, HttpStatusCode.RequestTimeout),
                _ => new AgentDeploymentReportResult(token, "resume"),
            };

        // The step changes send Running reports too, so only the beats at the apply count.
        int before = ApplyBeats(server);
        await time.AdvanceUntilAsync(TimeSpan.FromSeconds(10), () => ApplyBeats(server) >= before + 3);

        _tools.ApplyGate.SetResult();

        Assert.Equal(DeploymentOutcome.Deployed, (await run.WaitAsync(TestContext.Current.CancellationToken)).Outcome);
    }

    [Fact]
    public async Task ARefusedTokenOnABeatEndsTheRun()
    {
        ManualTimeProvider time = new();
        _tools.ApplyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        Task<DeploymentRunResult> run = RunAsync(server, time: time, heartbeatInterval: TimeSpan.FromSeconds(10));
        await _tools.ApplyStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        server.AnswerReports = (_, _) => throw new AgentTokenRejectedException();
        await time.AdvanceUntilAsync(TimeSpan.FromSeconds(10), () => run.IsCompleted);

        DeploymentRunResult result = await run;

        Assert.Equal(DeploymentOutcome.TokenRejected, result.Outcome);
        Assert.Equal(DeploymentStep.Apply, result.Step);
        Assert.DoesNotContain(server.Reports, report => report.State != DeploymentState.Running);
        Assert.DoesNotContain("reboot", _tools.Calls);
    }

    [Fact]
    public async Task ARefusedBeatFailsTheStepWithTheServersReason()
    {
        ManualTimeProvider time = new();
        _tools.ApplyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        Task<DeploymentRunResult> run = RunAsync(server, time: time, heartbeatInterval: TimeSpan.FromSeconds(10));
        await _tools.ApplyStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        server.AnswerReports = (report, token) => report.State == DeploymentState.Running
            ? throw new AgentRequestException("409", "This deployment is no longer running.", HttpStatusCode.Conflict)
            : new AgentDeploymentReportResult(token, "resume");
        await time.AdvanceUntilAsync(TimeSpan.FromSeconds(10), () => run.IsCompleted);

        DeploymentRunResult result = await run;

        Assert.Equal(DeploymentOutcome.Failed, result.Outcome);
        Assert.Equal(new AgentDeploymentReport(DeploymentState.Failed, DeploymentStep.Apply, 50, "This deployment is no longer running."), server.Reports[^1]);
    }

    private static int ApplyBeats(ScriptedAgentServer server) =>
        server.Reports.Count(report => report is { State: DeploymentState.Running, Step: DeploymentStep.Apply });

    private Task<DeploymentRunResult> RunAsync(
        ScriptedAgentServer server,
        AgentDeployment? deployment = null,
        LocalDisk? confirmedDisk = null,
        TimeProvider? time = null,
        TimeSpan? heartbeatInterval = null,
        AgentLog? log = null,
        string? scratchDirectory = null,
        IBcdWriter? bcdWriter = null)
    {
        time ??= new ImmediateTimeProvider();
        DeploymentRunner runner = TestAgents.Runner(
            server,
            _tools,
            log ?? new AgentLog(time, TextWriter.Null),
            time,
            heartbeatInterval,
            scratchDirectory,
            bcdWriter);

        return runner.RunAsync(s_machineId, deployment ?? _image.Deployment(), confirmedDisk, "session-0", "resume-0", server.Stop.Token);
    }
}
