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
        Assert.Equal(["list", "prepare", "partition 0", "apply 1", "bcd", "reboot"], _tools.Calls);
        Assert.True(_tools.ImageWasThereToApply);
        Assert.False(Directory.Exists(Path.Combine(Windows, "DDT")));
        Assert.Equal(["head session-0", "report Running session-0"], server.Calls.Take(2));

        List<AgentDeploymentReport> reports = server.Reports;
        Assert.Equal(new AgentDeploymentReport(DeploymentState.Running, DeploymentStep.Partition, 0, null), reports[0]);
        Assert.Equal(new AgentDeploymentReport(DeploymentState.Done, DeploymentStep.Reboot, 100, null), reports[^1]);
        Assert.All(reports[..^1], report => Assert.Equal(DeploymentState.Running, report.State));

        // A step never goes backwards.
        Assert.Equal(reports.Select(report => report.Step).Order(), reports.Select(report => report.Step));
    }

    [Fact]
    public async Task WritesTheAnswerFileAndHasSetupDeleteIt()
    {
        string scripts = Path.Combine(Windows, "Windows", "Setup", "Scripts");
        _tools.Applied = _ =>
        {
            Directory.CreateDirectory(scripts);
            File.WriteAllText(Path.Combine(scripts, "SetupComplete.cmd"), "echo from the image");
        };

        await RunAsync(_image.Serve(new ScriptedAgentServer()));

        Assert.Equal(TestImage.Unattend, await File.ReadAllTextAsync(UnattendFile.PathIn(Windows), TestContext.Current.CancellationToken));
        Assert.Equal(
            $"echo from the image\r\n{UnattendFile.CleanupLine}\r\n",
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
                _tools.Disks[0] = FakeDeploymentTools.Disk(0, sizeBytes: 3L * 1024 * 1024 * 1024);
                expected = "Disk 0 holds 3 GB, but Windows 11 Pro needs 3.5 GB";
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
        // A directory where SetupComplete.cmd belongs makes appending the cleanup line fail.
        _tools.Applied = root => Directory.CreateDirectory(Path.Combine(root, "Windows", "Setup", "Scripts", "SetupComplete.cmd"));

        DeploymentRunResult result = await RunAsync(_image.Serve(new ScriptedAgentServer()));

        Assert.Equal(DeploymentOutcome.Failed, result.Outcome);
        Assert.Equal(DeploymentStep.Unattend, result.Step);
        Assert.False(File.Exists(UnattendFile.PathIn(Windows)));
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

    private Task<DeploymentRunResult> RunAsync(
        ScriptedAgentServer server,
        AgentDeployment? deployment = null,
        LocalDisk? confirmedDisk = null,
        TimeProvider? time = null,
        TimeSpan? heartbeatInterval = null,
        AgentLog? log = null)
    {
        time ??= new ImmediateTimeProvider();
        DeploymentRunner runner = TestAgents.Runner(server, _tools, log ?? new AgentLog(time, TextWriter.Null), time, heartbeatInterval);

        return runner.RunAsync(s_machineId, deployment ?? _image.Deployment(), confirmedDisk, "session-0", "resume-0", server.Stop.Token);
    }
}
