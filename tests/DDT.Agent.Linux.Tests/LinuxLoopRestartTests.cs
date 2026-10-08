// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// The agent's loop with the restart of the Linux boot environment: a restart that was issued happens however the loop
// ends, and not at all when the loop goes on.
public sealed class LinuxLoopRestartTests : IDisposable
{
    private const string Withdrawn = "The agent goes on, so the restart it had issued does not happen.";

    private static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");

    private readonly LinuxTree _tree = new();
    private readonly FakeDeploymentTools _tools = new();
    private readonly StringWriter _console = new();
    private readonly LinuxUefiVariables _firmware;
    private readonly LinuxRestart _restart;
    private readonly List<string> _atRestart = [];
    private ScriptedAgentServer _server = new();

    public LinuxLoopRestartTests()
    {
        Directory.CreateDirectory(_tree.Variables);
        _firmware = _tree.Firmware();
        _tree.WriteVariable("BootCurrent", LinuxUefiVariables.Global, 0x03, 0x00);
        _tree.WriteVariable("Boot0003", LinuxUefiVariables.Global, 1, 0, 0, 0, 0, 0, (byte)'N', 0, 0, 0);

        // A kernel that refuses, as it does a process that may not restart the machine, so the test goes on after it.
        _restart = new LinuxRestart(
            new AgentLog(new ImmediateTimeProvider(), _console),
            () =>
            {
                _atRestart.AddRange(_server.Calls);
                _atRestart.Add("kernel restart");

                return 1;
            },
            _ => true);
    }

    public void Dispose()
    {
        _tools.Dispose();
        _tree.Dispose();
    }

    private static AgentRegistrationResult Registered() => new(s_machineId, MachineState.Approved, "session-0", "resume-0", 10, "bob");

    private static AgentNextResult Next(AgentEnvironment? restartInto = null, AgentRun? run = null) =>
        new(MachineState.Approved, "session-1", "resume-1", 10, "bob", Run: run, RestartInto: restartInto);

    // The loop as LinuxBoot puts it together, with the server, the disks and the console of the tests.
    private Task<int> RunAsync(ScriptedAgentServer server)
    {
        _server = server;
        ImmediateTimeProvider time = new();
        AgentLog log = new(time, _console);
        LinuxBootRebooter rebooter = new(_firmware, _restart, log);
        ResumeCarrier carrier = new(_firmware, AgentEnvironment.LinuxBoot, log);
        TextMachineConsole nobody = new(new ScriptedSignInPrompt { IsAvailable = false }, new AgentLog(time, TextWriter.Null));
        ConsoleStatus status = TestAgents.Status(nobody);
        TestRunnerOptions options = new() { Environment = AgentEnvironment.LinuxBoot, Rebooter = rebooter, Status = status };
        TestMachine machine = new(_tools, log, time)
        {
            Environment = AgentEnvironment.LinuxBoot,
            Switch = new EnvironmentSwitch(carrier, rebooter),
            Runner = TestAgents.Runner(server, _tools, log, time, options),
            GoesOn = () => rebooter.Withdraw(carrier),
        };

        return LinuxBoot.RunAsync(TestAgents.Loop(server, status, machine), _restart, server.Stop.Token);
    }

    [Fact]
    public async Task ARestartIntoTheOtherEnvironmentHappensOnceTheLoopHasEndedAndTheServerHasTheLastLines()
    {
        ScriptedAgentServer server = new ScriptedAgentServer().OnRegister(_ => Registered()).OnNext(_ => Next(AgentEnvironment.WindowsPE));

        int exitCode = await RunAsync(server);

        Assert.Equal(AgentExitCodes.Restarting, exitCode);
        Assert.Equal(["register", "next session-0", "log session-1", "kernel restart"], _atRestart);
        Assert.Contains(server.SentLines, line => line.Message == "The machine restarts into Windows PE, where it stays signed in and goes on.");
        Assert.Equal([0x03, 0x00], _firmware.Read("BootNext"));
        Assert.Equal(ResumeCarrier.Encode(AgentEnvironment.WindowsPE, "resume-1"), _firmware.Read(ResumeCarrier.VariableName, ResumeCarrier.Vendor));
    }

    // The server refuses the token with the last lines, so the loop registers again. Without the withdrawal the restart
    // would wait for whatever ends the agent later, and the firmware would keep where to start next and the old token.
    [Fact]
    public async Task WhenTheLoopGoesOnAfterARestartWasIssuedNothingOfTheRestartStays()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(AgentEnvironment.WindowsPE))
            .OnLog(_ => throw new AgentTokenRejectedException())
            .OnRegister(_ => Registered());

        int exitCode = await RunAsync(server);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Equal(2, server.Registrations.Count);
        Assert.False(_restart.Issued);
        Assert.Empty(_atRestart);
        Assert.Null(_firmware.Read("BootNext"));
        Assert.Null(_firmware.Read(ResumeCarrier.VariableName, ResumeCarrier.Vendor));
        Assert.Contains(Withdrawn, _console.ToString(), StringComparison.Ordinal);
    }

    // Whatever the loop dies of after the restart was issued, the machine restarts: the server may have the run as done.
    [Fact]
    public async Task ARestartThatWasIssuedHappensAlsoWhenTheLoopThrows()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(AgentEnvironment.WindowsPE))
            .OnLog(_ => throw new InvalidOperationException("The loop dies here."));

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(server));

        Assert.Equal("The loop dies here.", thrown.Message);
        Assert.Equal("kernel restart", _atRestart[^1]);
    }

    [Fact]
    public async Task ALoopThatEndsWithoutARestartRestartsNothing()
    {
        ScriptedAgentServer server = new ScriptedAgentServer().OnRegister(_ => Registered()).OnNext(_ => Next());

        int exitCode = await RunAsync(server);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Empty(_atRestart);
        Assert.DoesNotContain(Withdrawn, _console.ToString(), StringComparison.Ordinal);
    }

    // An earlier restart that did not happen left the network as the next start. The run put its disk first in the boot
    // order, and the machine must follow that.
    [Fact]
    public async Task AfterARunTheMachineFollowsTheBootOrderWhateverAnEarlierRestartHadNamed()
    {
        _tree.WriteVariable("BootNext", LinuxUefiVariables.Global, 0x03, 0x00);
        TestRawImage image = new();
        WriteRawImageStep write = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000e001"), Name = "Write the disk", ImageId = TestRawImage.ImageId };
        AgentRun run = new(TestRuns.RunId, DeploymentState.Assigned, "Install Linux", new SequenceDefinition(SequenceDefinition.CurrentVersion, [write]), [image.RunImage()], [], null, "LINUX-01", false);
        ScriptedAgentServer server = image.Serve(new ScriptedAgentServer()).OnRegister(_ => Registered()).OnNext(_ => Next(run: run));
        List<string> order = [];
        _restart.Before = () => order.Add($"before, {_atRestart.Count} restarts");

        int exitCode = await RunAsync(server);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Equal(DeploymentState.Done, server.RunReports[^1].State);
        Assert.Null(_firmware.Read("BootNext"));
        Assert.Equal("kernel restart", _atRestart[^1]);
        Assert.Equal(["before, 0 restarts"], order);
        Assert.Contains("That is taken back, so the next start follows the boot order.", _console.ToString(), StringComparison.Ordinal);
    }
}
