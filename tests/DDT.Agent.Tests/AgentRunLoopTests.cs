// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class AgentRunLoopTests : IDisposable
{
    private static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");

    private static readonly AgentSequenceChoice s_choice = new(
        Guid.Parse("0193a4b2-0000-7000-8000-00000000e001"),
        "Install Windows",
        null,
        ErasesDisk: true,
        NeedsComputerName: false,
        RequiredBytes: 30_000,
        Suggested: false);

    private readonly FakeDeploymentTools _tools = new();
    private readonly TestImage _image = new();

    public void Dispose() => _tools.Dispose();

    private string Windows => _tools.Volumes.Windows;

    private static AgentRegistrationResult Registered(MachineState state = MachineState.Approved) =>
        new(s_machineId, state, "session-0", "resume-0", 10, "bob");

    private static AgentNextResult Next(MachineState state, string token, AgentRun? run = null, bool canPick = false) =>
        new(state, token, "resume", 10, "bob", Run: run, CanPickSequence: canPick);

    private AgentRun InstallWindows(DeploymentState state = DeploymentState.Assigned, int? diskNumber = null) =>
        TestRuns.Run(TestRuns.InstallWindows, _image, state, diskNumber);

    // Goes on in Windows after the answer file.
    private AgentRun InWindows(DeploymentState state) =>
        TestRuns.Run([.. TestRuns.InstallWindows, TestRuns.Script(4, SequencePhase.Windows)], _image, state);

    [Fact]
    public async Task RunsAnAssignedRunAndEndsWithTheDeployedExitCode()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(MachineState.Approved, "session-1", InstallWindows()));

        int exitCode = await CreateLoop(server, new ScriptedSignInPrompt { IsAvailable = false }).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Equal(["register", "next session-0", $"head-file {_image.Sha256} session-1", "run-report Running session-1"], server.Calls.Take(4));
        Assert.Equal("reboot", _tools.Calls[^1]);
    }

    [Fact]
    public async Task RegistersWithTheEligibleDisksAndTheSequencesItRuns()
    {
        _tools.Disks.Add(FakeDeploymentTools.Disk(2, partitions: 3));
        ScriptedAgentServer server = new ScriptedAgentServer().OnRegister(_ => Registered(MachineState.Rejected) with { Token = null });

        await CreateLoop(server, new ScriptedSignInPrompt { IsAvailable = false }).RunAsync(server.Stop.Token);

        AgentRegistration registration = Assert.Single(server.Registrations);
        Assert.Equal([_tools.Disks[0].ToAgentDisk(), new AgentDisk(2, "Test disk 2", _tools.Disks[1].SizeBytes, "Nvme", 3)], registration.Disks);
        Assert.Equal(SequenceDefinition.CurrentVersion, registration.SequenceVersion);
        Assert.Equal(AgentEnvironment.WindowsPE, registration.Environment);
        Assert.Null(registration.RunToken);
        Assert.False(registration.SecureBootEnabled);
    }

    [Fact]
    public async Task RegistersWithTheSecureBootStateTheFirmwareGives()
    {
        ScriptedAgentServer server = new ScriptedAgentServer().OnRegister(_ => Registered(MachineState.Rejected) with { Token = null });
        ImmediateTimeProvider time = new();

        await TestAgents.Loop(
            server,
            new ScriptedSignInPrompt { IsAvailable = false },
            new(_tools, new AgentLog(time, TextWriter.Null), time) { Identity = new DryRunMachineIdentityReader(1, secureBootEnabled: true, trustedUefiCas: UefiCa.Microsoft2011) }).RunAsync(server.Stop.Token);

        AgentRegistration registration = Assert.Single(server.Registrations);
        Assert.True(registration.SecureBootEnabled);
        Assert.Equal(UefiCa.Microsoft2011, registration.TrustedUefiCas);
    }

    [Fact]
    public async Task KeepsPollingAfterAFailedRunAndOffersThePicker()
    {
        _tools.FailAt = "partition";
        ScriptedSignInPrompt prompt = new();
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(MachineState.Approved, "session-1", InstallWindows()))
            .OnRunReport(DeploymentState.Failed, _ => new AgentRunReportResult("session-f", "resume-f", null))
            .OnNext(_ => Next(MachineState.Failed, "session-2", canPick: true))
            .OnSequences(() => [s_choice]);

        int exitCode = await CreateLoop(server, prompt).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Contains("next session-f", server.Calls);
        Assert.Contains("sequences session-2", server.Calls);
        Assert.Contains("log session-2", server.Calls);
        Assert.Equal(["Sequence number"], prompt.Labels);
    }

    [Fact]
    public async Task APickStartsTheRunAtTheNextPoll()
    {
        ScriptedSignInPrompt prompt = new("1", "ERASE");
        AgentRun picked = InstallWindows(diskNumber: 0);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(MachineState.Approved, "session-1", canPick: true))
            .OnSequences(() => [s_choice])
            .OnNext(_ => Next(MachineState.Approved, "session-2", canPick: true))
            .OnPickSequence(_ => picked)
            .OnNext(_ => Next(MachineState.Approved, "session-3", picked));

        int exitCode = await CreateLoop(server, prompt).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Equal([new AgentRunRequest(s_choice.Id, 0, null)], server.RunRequests);
        Assert.Equal(["Sequence number", "Type ERASE to continue"], prompt.Labels);
        Assert.Contains("pick-sequence session-2", server.Calls);
        Assert.Contains("partition 0", _tools.Calls);
    }

    [Fact]
    public async Task RunsAPickWhoseAnswerWasLost()
    {
        ScriptedSignInPrompt prompt = new("1", "ERASE");
        AgentRun picked = InstallWindows(diskNumber: 0);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(MachineState.Approved, "session-1", canPick: true))
            .OnSequences(() => [s_choice])
            .OnNext(_ => Next(MachineState.Approved, "session-2", canPick: true))
            .OnPickSequence(_ => throw new HttpRequestException("The connection was reset."))
            .OnNext(_ => Next(MachineState.Approved, "session-3", picked));

        int exitCode = await CreateLoop(server, prompt).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Contains("partition 0", _tools.Calls);
    }

    [Fact]
    public async Task ErasesNoDiskForAPickWhoseSequenceWasChangedToEraseOne()
    {
        // The list said the sequence erases no disk, but an administrator added a Partition step before it was chosen.
        AgentSequenceChoice inventory = s_choice with { ErasesDisk = false };
        ScriptedSignInPrompt prompt = new("1");
        AgentRun picked = InstallWindows();
        StringWriter console = new();
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(MachineState.Approved, "session-1", canPick: true))
            .OnSequences(() => [inventory])
            .OnPickSequence(_ => picked)
            .OnNext(_ => Next(MachineState.Approved, "session-2", picked))
            .OnNext(_ => Next(MachineState.Failed, "session-3", canPick: true))
            .OnSequences(() => [s_choice]);

        ImmediateTimeProvider time = new();
        await TestAgents.Loop(server, prompt, new(_tools, new AgentLog(time, console), time)).RunAsync(server.Stop.Token);

        Assert.Equal([new AgentRunRequest(inventory.Id, null, null)], server.RunRequests);
        AgentRunReport report = Assert.Single(server.RunReports);
        Assert.Equal((DeploymentState.Failed, SequencePicker.ChangedAfterChoiceMessage), (report.State, report.Error));
        Assert.DoesNotContain(_tools.Calls, call => call.StartsWith("partition", StringComparison.Ordinal));
        Assert.Contains(console.ToString().Split(Environment.NewLine), line => line.EndsWith(SequencePicker.ChangedAfterChoiceMessage, StringComparison.Ordinal));

        // The list is offered again, now with what the sequence does.
        Assert.Equal(["Sequence number", "Sequence number"], prompt.Labels);
    }

    [Fact]
    public async Task DoesNotRunADiskChoiceMadeBeforeTheAgentStarted()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(MachineState.Approved, "session-1", InstallWindows(diskNumber: 0)));

        await CreateLoop(server, new ScriptedSignInPrompt { IsAvailable = false }).RunAsync(server.Stop.Token);

        AgentRunReport report = Assert.Single(server.RunReports);
        Assert.Equal(DeploymentState.Failed, report.State);
        Assert.StartsWith("Disk 0 was chosen before the agent started again", report.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(_tools.Calls, call => call.StartsWith("partition", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AsksForNoDiskAndNoErasureForASequenceThatErasesNone()
    {
        AgentSequenceChoice inventory = s_choice with { ErasesDisk = false, NeedsComputerName = true };
        ScriptedSignInPrompt prompt = new("1", "PC-7");
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(MachineState.Approved, "session-1", canPick: true))
            .OnSequences(() => [inventory])
            .OnNext(_ => Next(MachineState.Approved, "session-2", canPick: true))
            .OnNext(_ => Next(MachineState.Approved, "session-3", canPick: true))
            .OnPickSequence(_ => throw new AgentRequestException("409", "This machine cannot pick a sequence now.", HttpStatusCode.Conflict));

        await CreateLoop(server, prompt).RunAsync(server.Stop.Token);

        Assert.Equal([new AgentRunRequest(inventory.Id, null, "PC-7")], server.RunRequests);
        Assert.Equal(["Sequence number", "Computer name"], prompt.Labels);

        // Nothing erases a disk, so none was read for the picker: only for the registration.
        Assert.Equal(["list"], _tools.Calls);
    }

    [Fact]
    public async Task AWebAssignmentTakesThePickerAway()
    {
        ScriptedSignInPrompt prompt = new();
        int cancelledWhenAssigned = -1;
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(MachineState.Approved, "session-1", canPick: true))
            .OnSequences(() => [s_choice])
            .OnNext(_ => Next(MachineState.Approved, "session-2", InstallWindows()));
        server.AnswerRunReports = (_, token) =>
        {
            if (cancelledWhenAssigned < 0)
            {
                cancelledWhenAssigned = prompt.Cancelled;
            }

            return new AgentRunReportResult(token, "resume", null);
        };

        int exitCode = await CreateLoop(server, prompt).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Equal(1, cancelledWhenAssigned);
        Assert.Empty(server.RunRequests);
    }

    [Fact]
    public async Task ReportsARunningRunItCannotGoOnWithAsFailed()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered(MachineState.Deploying))
            .OnNext(_ => Next(MachineState.Deploying, "session-1", InstallWindows(DeploymentState.Running)))
            .OnRunReport(DeploymentState.Failed, _ => new AgentRunReportResult("session-f", "resume-f", null))
            .OnNext(_ => Next(MachineState.Failed, "session-2"));

        await CreateLoop(server, new ScriptedSignInPrompt { IsAvailable = false }).RunAsync(server.Stop.Token);

        AgentRunReport report = Assert.Single(server.RunReports);
        Assert.Equal((DeploymentState.Failed, SequenceRunner.LostContactMessage), (report.State, report.Error));
        Assert.Contains("log session-f", server.Calls);
        Assert.DoesNotContain(_tools.Calls, call => call != "list");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReportsTheRealFailureWhenTheRunCouldNotReportIt(bool tokenRefused)
    {
        _tools.FailAt = "bcd";
        AgentRun run = InstallWindows();
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(MachineState.Approved, "session-1", run));

        if (tokenRefused)
        {
            server.OnRunReport(DeploymentState.Failed, _ => throw new AgentTokenRejectedException())
                .OnRegister(_ => Registered(MachineState.Deploying));
        }
        else
        {
            for (int attempt = 0; attempt <= ServerCallRules.MaxRetries; attempt++)
            {
                server.OnRunReport(DeploymentState.Failed, _ => throw new HttpRequestException("503", null, HttpStatusCode.ServiceUnavailable));
            }
        }

        server.OnNext(_ => Next(MachineState.Deploying, "session-2", run with { State = DeploymentState.Running }));

        await CreateLoop(server, new ScriptedSignInPrompt { IsAvailable = false }).RunAsync(server.Stop.Token);

        // Every attempt, the run's and then the loop's, carries the run's own failure.
        List<AgentRunReport> failed = [.. server.RunReports.Where(report => report.State == DeploymentState.Failed)];
        Assert.Equal(tokenRefused ? 2 : ServerCallRules.MaxRetries + 2, failed.Count);
        Assert.All(failed, report => Assert.Equal("The scripted step failed.", report.Error));
        Assert.Equal(DeploymentState.Failed, server.RunReports[^1].State);
    }

    [Fact]
    public async Task RestartsInTheMiddleOfARunAndGoesOnWithItAfterTheRestart()
    {
        AgentRun run = TestRuns.Run([TestRuns.Partition, TestRuns.Reboot, TestRuns.Apply, TestRuns.Unattend], _image);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(MachineState.Approved, "session-1", run))
            .OnRunReport(DeploymentState.Running, _ => new AgentRunReportResult("session-2", "resume-2", "run-token-1"));

        int first = await CreateLoop(server, new ScriptedSignInPrompt { IsAvailable = false }).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Restarting, first);
        Assert.Equal(["list", "list", "prepare", "partition 0", "reboot into Windows PE"], _tools.Calls);
        RestartHappened();

        // A new agent after the restart: it presents the run token it finds on the disk, and the server resumes the run.
        server.OnRegister(registration => Registered(MachineState.Deploying) with { RunId = run.Id, RunToken = registration.RunToken })
            .OnNext(_ => Next(MachineState.Deploying, "session-3", run with { State = DeploymentState.Running }));

        int second = await CreateLoop(server, new ScriptedSignInPrompt { IsAvailable = false }).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Deployed, second);
        Assert.Equal("run-token-1", server.Registrations[1].RunToken);
        Assert.Equal(
            [$"find {FakeDeploymentTools.WindowsPartitionId}", "apply 1", "bcd", "firmware after the answer file", "reboot"],
            _tools.Calls[6..]);
        Assert.Single(_tools.Calls, call => call.StartsWith("partition", StringComparison.Ordinal));
    }

    // Ctrl+C right after the restart step, and the agent started again by hand in the same Windows PE. The run's state
    // already goes on after the step, so the agent restarts first, and the run goes on only after the restart.
    [Fact]
    public async Task AStopRightAfterARestartStepRestartsAtTheNextStartInsteadOfGoingOn()
    {
        AgentRun run = TestRuns.Run([TestRuns.Partition, TestRuns.Reboot, TestRuns.Apply, TestRuns.Unattend], _image);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(MachineState.Approved, "session-1", run));
        server.AnswerRunReports = (report, token) =>
        {
            if (report.Activity == RunActivity.Restarting)
            {
                server.Stop.Cancel();
                server.Stop.Token.ThrowIfCancellationRequested();
            }

            return new AgentRunReportResult(token, "resume", "run-token-1");
        };

        int stopped = await CreateLoop(server, new ScriptedSignInPrompt { IsAvailable = false }).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Stopped, stopped);
        Assert.Equal(["list", "list", "prepare", "partition 0"], _tools.Calls);
        Assert.Equal(RestartInto.WindowsPE, RestartDue());

        // It neither registers nor runs a step, and every start restarts until a restart empties the RAM disk.
        ScriptedAgentServer again = new();
        ImmediateTimeProvider time = new();
        StringWriter console = new();

        int restarting = await TestAgents.Loop(again, new ScriptedSignInPrompt { IsAvailable = false }, new(_tools, new AgentLog(time, console), time))
            .RunAsync(again.Stop.Token);

        Assert.Equal(AgentExitCodes.Restarting, restarting);
        Assert.Empty(again.Calls);
        Assert.Equal(["list", "list", "prepare", "partition 0", "reboot into Windows PE"], _tools.Calls);
        Assert.Equal(["The machine was to restart into Windows PE for the run but has not restarted since. Restarting it now."], Problems(console));
        RestartHappened();

        ScriptedAgentServer resumed = _image.Serve(new ScriptedAgentServer())
            .OnRegister(registration => Registered(MachineState.Deploying) with { RunId = run.Id, RunToken = registration.RunToken })
            .OnNext(_ => Next(MachineState.Deploying, "session-3", run with { State = DeploymentState.Running }));

        int deployed = await CreateLoop(resumed, new ScriptedSignInPrompt { IsAvailable = false }).RunAsync(resumed.Stop.Token);

        Assert.Equal(AgentExitCodes.Deployed, deployed);
        Assert.Equal("run-token-1", Assert.Single(resumed.Registrations).RunToken);
        Assert.Equal(
            [$"find {FakeDeploymentTools.WindowsPartitionId}", "apply 1", "bcd", "firmware after the answer file", "reboot"],
            _tools.Calls[6..]);
    }

    // However the restart came to be due, a start that finds it makes it the same way and says so once: back into
    // Windows PE from the boot entry this start came from, or into the installed Windows by the boot order the run left.
    [Theory]
    [InlineData(RestartInto.WindowsPE)]
    [InlineData(RestartInto.Windows)]
    public async Task ARestartStillDueComesBeforeTheRegistration(RestartInto into)
    {
        FakeUefiVariables firmware = new();
        firmware.Values["BootCurrent"] = [0x03, 0x00];
        firmware.Values["Boot0003"] = [0x01];
        RecordingToolRunner tools = new();
        ImmediateTimeProvider time = new();
        StringWriter console = new();
        AgentLog log = new(time, console);
        TestAgents.RestartMarker(_tools, log).Set(into);
        ScriptedAgentServer server = new();
        SequenceRunner runner = TestAgents.Runner(server, _tools, log, time, new() { ToolRunner = tools, Rebooter = new WindowsPERebooter(tools, firmware, log) });

        int exitCode = await TestAgents.Loop(server, new ScriptedSignInPrompt { IsAvailable = false }, new(_tools, log, time) { Runner = runner })
            .RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Restarting, exitCode);
        Assert.Empty(server.Calls);
        Assert.Empty(_tools.Calls);
        string[] bootNext = into == RestartInto.WindowsPE ? ["BootNext"] : [];
        Assert.Equal(bootNext, firmware.Writes);
        Assert.Equal([RecordingToolRunner.CommandLine(Path.Combine(Environment.SystemDirectory, "wpeutil.exe"), "reboot")], tools.Calls);
        Assert.Equal(
            [
                into == RestartInto.WindowsPE
                    ? "The machine was to restart into Windows PE for the run but has not restarted since. Restarting it now."
                    : "The machine was to start the installed Windows but has not restarted since. Restarting it now.",
            ],
            Problems(console));
    }

    // Ctrl+C before the loop starts, during the update check: the agent neither restarts nor says it does, and the
    // restart stays due for the next start.
    [Fact]
    public async Task AStartThatWasStoppedAlreadyLeavesTheRestartDue()
    {
        FakeUefiVariables firmware = new();
        firmware.Values["BootCurrent"] = [0x03, 0x00];
        firmware.Values["Boot0003"] = [0x01];
        RecordingToolRunner tools = new();
        ImmediateTimeProvider time = new();
        StringWriter console = new();
        AgentLog log = new(time, console);
        TestAgents.RestartMarker(_tools, log).Set(RestartInto.WindowsPE);
        ScriptedAgentServer server = new();
        SequenceRunner runner = TestAgents.Runner(server, _tools, log, time, new() { ToolRunner = tools, Rebooter = new WindowsPERebooter(tools, firmware, log) });
        await server.Stop.CancelAsync();

        int exitCode = await TestAgents.Loop(server, new ScriptedSignInPrompt { IsAvailable = false }, new(_tools, log, time) { Runner = runner })
            .RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Empty(firmware.Writes);
        Assert.Empty(tools.Calls);
        Assert.Empty(Problems(console));
        Assert.Equal(RestartInto.WindowsPE, RestartDue());
    }

    // wpeutil failed, and the agent was started again by hand: the restart is still due, wherever it leads.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnAgentStartedAgainAfterItsRestartFailedRestartsAgain(bool handOver)
    {
        AgentRun run = handOver
            ? InWindows(DeploymentState.Assigned)
            : TestRuns.Run([TestRuns.Partition, TestRuns.Reboot, TestRuns.Apply, TestRuns.Unattend], _image);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(MachineState.Approved, "session-1", run));
        ImmediateTimeProvider time = new();
        AgentLog log = new(time, TextWriter.Null);
        _tools.FailAt = "reboot";

        // A dry run's hand-over, which needs no SYSTEM hive to register the service in.
        SequenceRunner runner = TestAgents.Runner(server, _tools, log, time, new() { DryRunHandOver = true });
        int first = await TestAgents.Loop(server, new ScriptedSignInPrompt { IsAvailable = false }, new(_tools, log, time) { Runner = runner })
            .RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Restarting, first);
        Assert.Equal(handOver ? RestartInto.Windows : RestartInto.WindowsPE, RestartDue());
        _tools.FailAt = null;
        int callsBefore = _tools.Calls.Count;
        ScriptedAgentServer again = new();

        int second = await CreateLoop(again, new ScriptedSignInPrompt { IsAvailable = false }).RunAsync(again.Stop.Token);

        Assert.Equal(AgentExitCodes.Restarting, second);
        Assert.Empty(again.Calls);
        Assert.Equal([handOver ? "reboot" : "reboot into Windows PE"], _tools.Calls[callsBefore..]);
    }

    [Fact]
    public async Task RemovesTheRunFromTheDiskWhenTheServerEndedIt()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await LeaveRunOnDiskAsync(SequencePhase.WindowsPE, cancellationToken);
        ScriptedAgentServer server = new ScriptedAgentServer().OnRegister(_ => Registered());

        await CreateLoop(server, new ScriptedSignInPrompt { IsAvailable = false }).RunAsync(server.Stop.Token);

        Assert.Equal("run-token-1", Assert.Single(server.Registrations).RunToken);
        Assert.False(File.Exists(RunFiles.StatePathIn(Windows)));
        Assert.False(File.Exists(RunFiles.In(Windows, Log()).TokenPath));
        Assert.False(File.Exists(UnattendFile.PathIn(Windows)));
    }

    [Fact]
    public async Task HandsARunThatGoesOnInWindowsOverAgainWhenWindowsPEStarts()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await LeaveRunOnDiskAsync(SequencePhase.Windows, cancellationToken);
        AgentRun run = InWindows(DeploymentState.Running);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered(MachineState.Deploying) with { RunId = run.Id, RunToken = "run-token-1" })
            .OnNext(_ => Next(MachineState.Deploying, "session-1", run));
        ImmediateTimeProvider time = new();
        AgentLog log = new(time, TextWriter.Null);

        // A dry run's hand-over, which needs no SYSTEM hive to register the service in.
        SequenceRunner runner = TestAgents.Runner(server, _tools, log, time, new() { DryRunHandOver = true });
        int exitCode = await TestAgents.Loop(server, new ScriptedSignInPrompt { IsAvailable = false }, new(_tools, log, time) { Runner = runner })
            .RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Restarting, exitCode);
        Assert.Equal(["list", $"find {FakeDeploymentTools.WindowsPartitionId}", "bcd", "firmware after the answer file", "reboot"], _tools.Calls);
        SequenceState? state = await RunFiles.In(Windows, Log()).LoadStateAsync(cancellationToken);
        Assert.Equal((SequencePhase.Windows, "1"), (state?.Phase, state?.Variables[RunVariables.WindowsPEReturns]));
    }

    [Fact]
    public async Task SaysOnceThatItNoLongerRunsImageDeployments()
    {
        AgentDeployment deployment = new(Guid.NewGuid(), DeploymentState.Assigned, TestImage.ImageId, "Windows 11 Pro", _image.Sha256, 3000, 1, 10_000, null);
        StringWriter console = new();
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(MachineState.Approved, "session-1") with { Deployment = deployment })
            .OnNext(_ => Next(MachineState.Approved, "session-2") with { Deployment = deployment });

        ImmediateTimeProvider time = new();
        await TestAgents.Loop(server, new ScriptedSignInPrompt { IsAvailable = false }, new(_tools, new AgentLog(time, console), time))
            .RunAsync(server.Stop.Token);

        Assert.Single(console.ToString().Split(Environment.NewLine), line => line.Contains("no longer runs", StringComparison.Ordinal));
        Assert.DoesNotContain(_tools.Calls, call => call != "list");
    }

    private static AgentLog Log() => new(new ImmediateTimeProvider(), TextWriter.Null);

    // As an earlier start of the agent leaves it: the steps in Windows PE done, the answer file written, and the run
    // token.
    private async Task LeaveRunOnDiskAsync(SequencePhase phase, CancellationToken cancellationToken)
    {
        AgentRun run = InWindows(DeploymentState.Running);
        SequenceState state = SequenceStates.Start(run.Id, run.Sequence) with
        {
            Phase = phase,
            NextIndex = 3,
            Steps = [.. run.Sequence.Steps.Select((step, index) => new StepRunState(step.Id, index < 3 ? StepState.Done : StepState.Pending, null))],
            Variables = RunVariables.Of(_tools.Volumes),
        };
        RunFiles files = RunFiles.In(Windows, Log());
        await files.SaveStateAsync(state, cancellationToken);
        await files.SaveTokenAsync("run-token-1", cancellationToken);
        await UnattendFile.WriteAsync(Windows, TestImage.Unattend, cancellationToken);
    }

    // The warnings and errors on a console, without their time and level.
    private static string[] Problems(StringWriter console) =>
        [.. console.ToString().Split(Environment.NewLine).Where(line => line.Contains(" WARN ", StringComparison.Ordinal) || line.Contains(" ERROR ", StringComparison.Ordinal)).Select(line => line[15..])];

    private RestartInto? RestartDue() => TestAgents.RestartMarker(_tools, Log()).Due;

    // The restart that was due happened: it built Windows PE's RAM disk anew, without the marker.
    private void RestartHappened()
    {
        Assert.NotNull(RestartDue());
        File.Delete(TestAgents.RestartMarker(_tools, Log()).FilePath);
    }

    private AgentLoop CreateLoop(ScriptedAgentServer server, ScriptedSignInPrompt prompt)
    {
        ImmediateTimeProvider time = new();

        return TestAgents.Loop(server, prompt, new(_tools, new AgentLog(time, TextWriter.Null), time));
    }
}
