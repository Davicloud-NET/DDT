// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using Xunit;

namespace DDT.Agent.Tests;

// A run in the Linux boot environment that reaches its first step in the installed Linux: what the runner does
// around the hand-over, what it tells the server and in which order, and what a hand-over that does not end well
// leaves on the disk. The runner takes version 5 here, which no agent says it runs yet.
public sealed class LinuxHandOverTests : IDisposable
{
    private const string RunToken = "run-token-never-logged";
    private const string SessionToken = "session-token-never-logged";
    private const string ResumeToken = "resume-token-never-logged";

    private static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");

    private static readonly WriteRawImageStep s_write = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000e001"),
        Name = "Write the disk",
        ImageId = TestRawImage.ImageId,
    };

    private static readonly WriteCloudInitSeedStep s_seed = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000e002"),
        Name = "Seed",
        MetaData = "instance-id: \"{{SmbiosUuid}}\"\nlocal-hostname: \"{{ComputerName}}\"\n",
        UserData = "#cloud-config\nusers:\n  - default\n",
    };

    private static readonly RunScriptStep s_installed = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000e003"),
        Name = "In the installed Linux",
        Phase = SequencePhase.Linux,
        Interpreter = ScriptInterpreter.Shell,
        Script = "echo \"$DDT_PHASE\"",
        RebootExitCodes = [194],
    };

    private readonly FakeDeploymentTools _tools = new();
    private readonly RecordingToolRunner _toolRunner = new();
    private readonly MemoryRawDisks _disks = new();
    private readonly TestRawImage _image = new();
    private readonly StringWriter _console = new();
    private readonly byte[] _agent = new byte[200_000];
    private readonly string _agentPath;

    public LinuxHandOverTests()
    {
        new Random(11).NextBytes(_agent);
        Directory.CreateDirectory(_tools.Root);
        _agentPath = Path.Combine(_tools.Root, "ddt-agent");
        File.WriteAllBytes(_agentPath, _agent);
    }

    public void Dispose()
    {
        _tools.Dispose();
        _console.Dispose();
    }

    private AgentRun Run() =>
        new(
            TestRuns.RunId,
            DeploymentState.Assigned,
            "Install Linux",
            new SequenceDefinition(SequenceDefinition.CurrentVersion, [s_write, s_seed, s_installed]),
            [_image.RunImage()],
            [],
            null,
            "LINUX-01");

    // Every report it takes brings the run's token, as the server's answers do.
    private ScriptedAgentServer Server()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());
        server.AnswerRunReports = (_, _) => new AgentRunReportResult(SessionToken, ResumeToken, RunToken);

        return server;
    }

    private Task<RunResult> RunAsync(ScriptedAgentServer server, TimeProvider? time = null)
    {
        time ??= new ImmediateTimeProvider();
        AgentLog log = new(time, _console);
        TestRunnerOptions options = new()
        {
            RawDisks = _disks,
            Environment = AgentEnvironment.LinuxBoot,
            ToolRunner = _toolRunner,
            HeartbeatInterval = time is ManualTimeProvider ? TimeSpan.FromSeconds(10) : null,
            HandOver = new LinuxHandOver(_disks, _agentPath, TestAgents.Configuration, log),
        };
        RunRequest request = new(s_machineId, Run(), null, null, new DeploymentTokens("session-0", "resume-0"), new DryRunMachineIdentityReader(1).Read());

        return TestAgents.Runner(server, _tools, log, time, options).RunAsync(request, server.Stop.Token);
    }

    private TestSeed Seed() => TestSeed.Read(_disks.Disks[0]);

    private RestartInto? RestartDue() => TestAgents.RestartMarker(_tools, new AgentLog(new ImmediateTimeProvider(), TextWriter.Null)).Due;

    private static string Firmware => $"firmware noble-test {FirmwareBootEntry.FallbackLoaderPath} on partition {TestRawImage.EspNumber}";

    [Fact]
    public async Task PutsTheRunOnItsSeedStartsTheDiskFirstAndRestartsIntoTheInstalledLinux()
    {
        ScriptedAgentServer server = Server();

        RunResult result = await RunAsync(server);

        Assert.Equal(new RunResult(RunOutcome.Restarting), result);
        Assert.Equal(["list", "clean 0", Firmware, "reboot"], _tools.Calls);
        Assert.Equal(RestartInto.InstalledSystem, RestartDue());

        // The step in the installed Linux did not run here. It is where the run goes on.
        Assert.Empty(_toolRunner.Calls);
        TestSeed seed = Seed();
        Assert.Equal(["meta-data", "user-data", "vendor-data", "ddt"], seed.Names());
        Assert.Equal(_agent, seed.File(LinuxAgentFiles.SeedAgent));
        Assert.Equal(RunToken, seed.Text(LinuxAgentFiles.SeedToken));
        SequenceState staged = System.Text.Json.JsonSerializer.Deserialize(seed.File(LinuxAgentFiles.SeedState), AgentJsonContext.Default.SequenceState)!;
        Assert.Equal((SequencePhase.Linux, TestRuns.RunId, new NodeCursor(s_installed.Id, false)), (staged.Phase, staged.RunId, staged.Cursor));
        Assert.Equal([StepState.Done, StepState.Done, StepState.Pending], staged.Steps.Select(step => step.State));

        string console = _console.ToString();
        Assert.Contains(
            $"The agent (195 KB), its settings and the run are on the cloud-init seed, partition 3 of disk 0, which has {ByteSize.Format(seed.Volume.FreeBytes)} left.",
            console,
            StringComparison.Ordinal);
        Assert.Contains(LinuxHandOver.GoesOnMessage, console, StringComparison.Ordinal);
        Assert.Contains("The machine restarts into the installed Linux, where the agent goes on with the run.", console, StringComparison.Ordinal);
    }

    // As Windows PE names Windows around its hand-over. The server takes the installed phase from the boot environment
    // only for that, so no report may name it before the disk holds the run and starts first.
    [Fact]
    public async Task ReportsTheHandOverInTheBootEnvironmentAndTheRestartInTheInstalledLinux()
    {
        ScriptedAgentServer server = Server();
        Lock seenLock = new();
        List<(DeploymentState State, SequencePhase Phase, RunActivity Activity, bool DiskFirst)> seen = [];
        server.AnswerRunReports = (report, token) =>
        {
            lock (seenLock)
            {
                seen.Add((report.State, report.Phase, report.Activity, _tools.Calls.Contains(Firmware)));
            }

            return new AgentRunReportResult(token, ResumeToken, RunToken);
        };

        Assert.Equal(RunOutcome.Restarting, (await RunAsync(server)).Outcome);

        Assert.All(seen, report => Assert.Equal(DeploymentState.Running, report.State));
        Assert.Equal((SequencePhase.LinuxBoot, false), (seen[0].Phase, seen[0].DiskFirst));
        Assert.Equal((SequencePhase.Linux, RunActivity.Restarting, true), (seen[^1].Phase, seen[^1].Activity, seen[^1].DiskFirst));

        int handedOver = seen.FindIndex(report => report.Phase == SequencePhase.Linux);
        Assert.All(seen[..handedOver], report => Assert.Equal(SequencePhase.LinuxBoot, report.Phase));
        Assert.All(seen[handedOver..], report => Assert.Equal((SequencePhase.Linux, true), (report.Phase, report.DiskFirst)));
        Assert.All(seen[handedOver..], report => Assert.Contains(report.Activity, new[] { RunActivity.HandingOver, RunActivity.Restarting }));
        Assert.Equal([StepState.Done, StepState.Done], server.RunReports[^1].Steps.Select(step => step.State));
    }

    [Fact]
    public async Task NoLineOfTheRunHoldsItsToken()
    {
        ScriptedAgentServer server = Server();

        Assert.Equal(RunOutcome.Restarting, (await RunAsync(server)).Outcome);

        // The seed holds the run's token and no other of the machine's.
        TestSeed seed = Seed();
        Assert.True(seed.Holds(RunToken));
        Assert.False(seed.Holds(SessionToken) || seed.Holds(ResumeToken));

        string console = _console.ToString();
        Assert.NotEmpty(server.SentLines);
        Assert.Contains(server.SentLines, line => line.Message == LinuxHandOver.GoesOnMessage);

        foreach (string secret in new[] { RunToken, SessionToken, ResumeToken })
        {
            Assert.DoesNotContain(secret, console, StringComparison.Ordinal);
            Assert.All(server.SentLines, line => Assert.DoesNotContain(secret, line.Message, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task ABootOrderThatCannotBeChangedFailsTheRunAndTakesItOffTheSeed()
    {
        _tools.FailAt = "firmware";
        ScriptedAgentServer server = Server();

        RunResult result = await RunAsync(server);

        Assert.Equal(new RunResult(RunOutcome.Failed), result);
        AgentRunReport failed = server.RunReports[^1];
        Assert.Equal((DeploymentState.Failed, "The scripted step failed.", SequencePhase.LinuxBoot), (failed.State, failed.Error, failed.Phase));
        Assert.Equal([Firmware, "restore"], _tools.Calls[^2..]);
        Assert.Null(RestartDue());
        AssertSeedAsItsStepWroteIt();
        Assert.Contains("The agent and the run's token are off the cloud-init seed again, which is as its step wrote it.", _console.ToString(), StringComparison.Ordinal);
    }

    // An operator stopped the run while it was handed over. Nothing may be left for the installed Linux to go on with.
    [Fact]
    public async Task ARunTheServerEndsDuringTheHandOverIsTakenOffTheSeed()
    {
        ManualTimeProvider time = new();
        _tools.FirmwareGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedAgentServer server = Server();

        Task<RunResult> run = RunAsync(server, time);
        await _tools.FirmwareStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        server.AnswerRunReports = (report, token) => report.State == DeploymentState.Running
            ? throw new AgentRequestException("409", "This run is no longer running.", HttpStatusCode.Conflict)
            : new AgentRunReportResult(token, "resume", null);
        await time.AdvanceUntilAsync(TimeSpan.FromSeconds(10), () => run.IsCompleted);

        Assert.Equal(new RunResult(RunOutcome.Failed), await run);
        Assert.Equal((DeploymentState.Failed, "This run is no longer running."), (server.RunReports[^1].State, server.RunReports[^1].Error));
        Assert.Equal([Firmware, "restore"], _tools.Calls[^2..]);
        Assert.Null(RestartDue());
        AssertSeedAsItsStepWroteIt();
    }

    // Like the state on a Windows volume: the registration with the run's token decides, and the run may go on.
    [Fact]
    public async Task ARefusedTokenDuringTheHandOverPutsTheBootOrderBackAndKeepsTheRunOnTheSeed()
    {
        ManualTimeProvider time = new();
        _tools.FirmwareGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedAgentServer server = Server();

        Task<RunResult> run = RunAsync(server, time);
        await _tools.FirmwareStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        server.AnswerRunReports = (_, _) => throw new AgentTokenRejectedException();
        await time.AdvanceUntilAsync(TimeSpan.FromSeconds(10), () => run.IsCompleted);

        Assert.Equal(RunOutcome.TokenRejected, (await run).Outcome);
        Assert.Equal([Firmware, "restore"], _tools.Calls[^2..]);
        Assert.Null(RestartDue());
        Assert.Equal(RunToken, Seed().Text(LinuxAgentFiles.SeedToken));
        Assert.DoesNotContain(server.RunReports, report => report.State != DeploymentState.Running);
    }

    [Fact]
    public async Task AStopDuringTheHandOverPutsTheBootOrderBackAndKeepsTheRunOnTheSeed()
    {
        _tools.FirmwareGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedAgentServer server = Server();

        Task<RunResult> run = RunAsync(server);
        await _tools.FirmwareStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await server.Stop.CancelAsync();

        Assert.Equal(new RunResult(RunOutcome.Stopped), await run);
        Assert.Equal([Firmware, "restore"], _tools.Calls[^2..]);
        Assert.Null(RestartDue());
        Assert.Equal(RunToken, Seed().Text(LinuxAgentFiles.SeedToken));
        Assert.DoesNotContain(server.RunReports, report => report.State != DeploymentState.Running);
    }

    private void AssertSeedAsItsStepWroteIt()
    {
        TestSeed seed = Seed();

        Assert.Equal(["meta-data", "user-data"], seed.Names());
        Assert.False(seed.Holds(RunToken));
        Assert.False(seed.Holds("ddt-agent.service"));
        Assert.Equal(s_seed.UserData, seed.Text(CloudInitSeed.UserData));
    }
}
