// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using Xunit;

namespace DDT.Agent.Tests;

// The agent on its way into the other boot environment, and after it: for a sequence picked at the machine, and for a
// run a poll answer names. The firmware and what restarts the machine are fakes.
public sealed class AgentLoopSwitchTests : IDisposable
{
    private const string Restarts = "INFO  The machine restarts into the Linux boot environment, where it stays signed in and goes on.";

    private static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");

    private static readonly AgentSequenceChoice s_windows = new(
        Guid.Parse("0193a4b2-0000-7000-8000-00000000e001"),
        "Install Windows 11",
        null,
        ErasesDisk: true,
        NeedsComputerName: false,
        RequiredBytes: 0,
        Suggested: false,
        Environment: AgentEnvironment.WindowsPE);

    private static readonly AgentSequenceChoice s_linux = s_windows with
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000e004"),
        Name = "Install Ubuntu",
        NeedsComputerName = true,
        Environment = AgentEnvironment.LinuxBoot,
    };

    private readonly FakeDeploymentTools _tools = new();
    private readonly FakeVendorVariables _firmware = new();
    private readonly StringWriter _console = new();

    public void Dispose()
    {
        _tools.Dispose();
        _console.Dispose();
    }

    private static AgentRegistrationResult Registered() => new(s_machineId, MachineState.Approved, "session-0", "resume-0", 10, "bob");

    private static AgentNextResult Next(string token, string resumeToken, bool canPick = false, AgentEnvironment? restartInto = null) =>
        new(MachineState.Approved, token, resumeToken, 10, "bob", CanPickSequence: canPick, RestartInto: restartInto);

    // A machine with a firmware and something that restarts it, unless change says otherwise.
    private AgentLoop Loop(
        ScriptedAgentServer server,
        IMachineConsole console,
        AgentEnvironment environment = AgentEnvironment.WindowsPE,
        Func<EnvironmentSwitch, EnvironmentSwitch?>? change = null)
    {
        ImmediateTimeProvider time = new();
        AgentLog log = new(time, _console);
        EnvironmentSwitch all = new(new ResumeCarrier(_firmware, environment, log), _tools);
        TestMachine machine = new(_tools, log, time) { Environment = environment, Switch = change is null ? all : change(all) };

        return TestAgents.Loop(server, TestAgents.Status(console), machine);
    }

    private static TextMachineConsole Nobody(AgentLog log) => new(new ScriptedSignInPrompt { IsAvailable = false }, log);

    private AgentLoop UnattendedLoop(ScriptedAgentServer server, AgentEnvironment environment = AgentEnvironment.WindowsPE, Func<EnvironmentSwitch, EnvironmentSwitch?>? change = null) =>
        Loop(server, Nobody(new AgentLog(new ImmediateTimeProvider(), TextWriter.Null)), environment, change);

    // What the console showed, without each line's time.
    private string[] Lines() =>
        [.. _console.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => line[9..])];

    private string[] Problems() => [.. Lines().Where(line => !line.StartsWith("INFO ", StringComparison.Ordinal))];

    [Fact]
    public async Task PickingASequenceOfTheOtherEnvironmentTellsTheServerKeepsTheNewestTokenAndRestarts()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered())
            .OnNext(_ => Next("session-1", "resume-1", canPick: true))
            .OnSequences(() => [s_windows, s_linux])
            .OnEnvironment(_ => { });
        ScriptedMachineConsole console = new(ScriptedMachineConsole.Sequence("Install Ubuntu"));

        int exitCode = await Loop(server, console).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Restarting, exitCode);
        Assert.Equal(new AgentEnvironmentRequest(AgentEnvironment.LinuxBoot, s_linux.Id), Assert.Single(server.EnvironmentRequests));
        Assert.Contains("environment session-1", server.Calls);
        Assert.DoesNotContain(server.Calls, call => call.StartsWith("pick-sequence", StringComparison.Ordinal));
        Assert.Equal(ResumeCarrier.Encode(AgentEnvironment.LinuxBoot, "resume-1"), _firmware.Resume);
        Assert.Equal("reboot into Windows PE", _tools.Calls[^1]);

        // The list was all that was asked: no disk, no name, no ERASE before the restart.
        SequenceQuestion asked = Assert.IsType<SequenceQuestion>(Assert.Single(console.Questions));
        Assert.Equal("The machine restarts into the Linux boot environment first.", Assert.Single(asked.Sequences, option => option.Name == "Install Ubuntu").Description);
        Assert.Null(Assert.Single(asked.Sequences, option => option.Name == "Install Windows 11").Description);

        // The server's log gets the line that says why the machine went away.
        Assert.Contains(Restarts, Lines());
        Assert.Contains(server.SentLines, line => Restarts.EndsWith(line.Message, StringComparison.Ordinal));
        Assert.Empty(Problems());
    }

    [Fact]
    public async Task ARestartTheServerRefusesDoesNotHappenAndTheListIsReadAgain()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered())
            .OnNext(_ => Next("session-1", "resume-1", canPick: true))
            .OnSequences(() => [s_windows, s_linux])
            .OnEnvironment(_ => throw new AgentRequestException("409", "This server has no Linux boot environment to start.", HttpStatusCode.Conflict))
            .OnNext(_ => Next("session-2", "resume-2", canPick: true))
            .OnSequences(() => [s_windows]);
        ScriptedMachineConsole console = new(ScriptedMachineConsole.Sequence("Install Ubuntu"));

        int exitCode = await Loop(server, console).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Null(_firmware.Resume);
        Assert.DoesNotContain(_tools.Calls, call => call.StartsWith("reboot", StringComparison.Ordinal));
        Assert.Equal(["WARN  The server did not accept the restart: This server has no Linux boot environment to start."], Problems());
        Assert.Equal(
            [2, 1],
            console.Questions.OfType<SequenceQuestion>().Select(question => question.Sequences.Count));
    }

    [Fact]
    public async Task ARestartTheServerDoesNotHearOfDoesNotHappen()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered())
            .OnNext(_ => Next("session-1", "resume-1", canPick: true))
            .OnSequences(() => [s_linux])
            .OnEnvironment(_ => throw new HttpRequestException("connection refused"));
        ScriptedMachineConsole console = new(ScriptedMachineConsole.Sequence("Install Ubuntu"));

        int exitCode = await Loop(server, console).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Null(_firmware.Resume);
        Assert.DoesNotContain(_tools.Calls, call => call.StartsWith("reboot", StringComparison.Ordinal));
        Assert.Equal(["WARN  Cannot tell the server of the restart (connection refused). Choose the sequence again."], Problems());
    }

    // A run assigned on the web or by a rule: nobody is at the machine, and the server already knows where it starts.
    [Fact]
    public async Task APollAnswerThatNamesTheOtherEnvironmentRestartsTheMachineWithItsNewestToken()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered())
            .OnNext(_ => Next("session-1", "resume-1", restartInto: AgentEnvironment.LinuxBoot));

        int exitCode = await UnattendedLoop(server).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Restarting, exitCode);
        Assert.Equal(ResumeCarrier.Encode(AgentEnvironment.LinuxBoot, "resume-1"), _firmware.Resume);
        Assert.Equal("reboot into Windows PE", _tools.Calls[^1]);
        Assert.Empty(server.EnvironmentRequests);
        Assert.Contains(Restarts, Lines());
        Assert.Contains(server.SentLines, line => Restarts.EndsWith(line.Message, StringComparison.Ordinal));
        Assert.Empty(Problems());
    }

    [Fact]
    public async Task TheLinuxBootEnvironmentGoesBackToWindowsPETheSameWay()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered())
            .OnNext(_ => Next("session-1", "resume-1", restartInto: AgentEnvironment.WindowsPE));

        int exitCode = await UnattendedLoop(server, AgentEnvironment.LinuxBoot).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Restarting, exitCode);
        Assert.Equal(AgentEnvironment.LinuxBoot, Assert.Single(server.Registrations).Environment);
        Assert.Equal(ResumeCarrier.Encode(AgentEnvironment.WindowsPE, "resume-1"), _firmware.Resume);
        Assert.Contains("INFO  The machine restarts into Windows PE, where it stays signed in and goes on.", Lines());
    }

    // The token is taken before the first registration and only then. Later this agent has tokens of its own.
    [Fact]
    public async Task AfterTheRestartTheAgentRegistersOnceWithTheTokenItFindsAndDeletesIt()
    {
        _firmware.Resume = ResumeCarrier.Encode(AgentEnvironment.LinuxBoot, "carried");
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered())
            .OnNext(_ => throw new AgentTokenRejectedException())
            .OnRegister(_ => Registered());

        await UnattendedLoop(server, AgentEnvironment.LinuxBoot).RunAsync(server.Stop.Token);

        Assert.Equal(["carried", "resume-0"], server.Registrations.Select(registration => registration.ResumeToken));
        Assert.All(server.Registrations, registration => Assert.Equal(AgentEnvironment.LinuxBoot, registration.Environment));
        Assert.Null(_firmware.Resume);
        Assert.Equal(["read DdtResume", "delete DdtResume"], _firmware.Calls);
        Assert.Equal(["WARN  The server no longer accepts this machine's token. Registering again."], Problems());
    }

    [Fact]
    public async Task AMachineThatDidNotRestartForDdtRegistersWithoutAToken()
    {
        ScriptedAgentServer server = new ScriptedAgentServer().OnRegister(_ => Registered());

        await UnattendedLoop(server).RunAsync(server.Stop.Token);

        Assert.Null(Assert.Single(server.Registrations).ResumeToken);
        Assert.Equal(["read DdtResume"], _firmware.Calls);
    }

    // The loop guard. The token still resumes, so nobody signs in again, but the machine stays where it is.
    [Fact]
    public async Task AMachineThatCameBackWhereItWasIsNotRestartedAgainAndSaysSoOnce()
    {
        _firmware.Resume = ResumeCarrier.Encode(AgentEnvironment.LinuxBoot, "carried");
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered())
            .OnNext(_ => Next("session-1", "resume-1", restartInto: AgentEnvironment.LinuxBoot))
            .OnNext(_ => Next("session-2", "resume-2", restartInto: AgentEnvironment.LinuxBoot));

        int exitCode = await UnattendedLoop(server).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Equal("carried", Assert.Single(server.Registrations).ResumeToken);
        Assert.Null(_firmware.Resume);
        Assert.DoesNotContain("write DdtResume", _firmware.Calls);
        Assert.DoesNotContain(_tools.Calls, call => call.StartsWith("reboot", StringComparison.Ordinal));
        Assert.Equal(
            [
                "WARN  This machine restarted to start the Linux boot environment, and Windows PE started again: the server could not start the other " +
                "boot environment. The machine is not restarted for it again until it was started anew.",
                "WARN  The machine is not restarted into the Linux boot environment again: the server could not start it when the machine last " +
                "restarted for it. Start the machine from the network anew to try again.",
            ],
            Problems());
    }

    // Whoever picks it again is told again, and the server hears nothing of it.
    [Fact]
    public async Task PickingItAgainThereDoesNotRestartEither()
    {
        _firmware.Resume = ResumeCarrier.Encode(AgentEnvironment.LinuxBoot, "carried");
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered())
            .OnNext(_ => Next("session-1", "resume-1", canPick: true))
            .OnSequences(() => [s_linux])
            .OnNext(_ => Next("session-2", "resume-2", canPick: true))
            .OnSequences(() => [s_linux]);
        ScriptedMachineConsole console = new(ScriptedMachineConsole.Sequence("Install Ubuntu"), ScriptedMachineConsole.Sequence("Install Ubuntu"));

        int exitCode = await Loop(server, console).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Empty(server.EnvironmentRequests);
        Assert.DoesNotContain(_tools.Calls, call => call.StartsWith("reboot", StringComparison.Ordinal));
        Assert.Equal(2, Problems().Count(line => line.StartsWith("WARN  The machine is not restarted into the Linux boot environment again:", StringComparison.Ordinal)));
    }

    // The machine restarts all the same. It registers anew there, and someone signs in at it again.
    [Fact]
    public async Task AFirmwareThatDoesNotKeepTheTokenCostsASignInNotTheRestart()
    {
        _firmware.WriteFailure = "The firmware variable DdtResume cannot be written (Windows error 1314).";
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered())
            .OnNext(_ => Next("session-1", "resume-1", restartInto: AgentEnvironment.LinuxBoot));

        int exitCode = await UnattendedLoop(server).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Restarting, exitCode);
        Assert.Null(_firmware.Resume);
        Assert.Equal("reboot into Windows PE", _tools.Calls[^1]);
        Assert.Contains("INFO  The machine restarts into the Linux boot environment.", Lines());
        Assert.StartsWith(
            "WARN  The firmware did not keep what lets this machine stay signed in across the restart (",
            Assert.Single(Problems()),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMachineThatCouldNotRestartItselfSaysHowToGoOnByHand()
    {
        _tools.FailAt = "reboot";
        _tools.Failure = new DeploymentStepException("wpeutil.exe ended with exit code 1.");
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered())
            .OnNext(_ => Next("session-1", "resume-1", restartInto: AgentEnvironment.LinuxBoot));
        ScriptedMachineConsole console = new() { CanAsk = false };

        int exitCode = await Loop(server, console).RunAsync(server.Stop.Token);

        // The token waits in the firmware for the restart by hand.
        Assert.Equal(AgentExitCodes.Restarting, exitCode);
        Assert.NotNull(_firmware.Resume);
        Assert.Equal(
            ["ERROR The machine could not restart itself (wpeutil.exe ended with exit code 1.). Restart it by hand; it then starts the Linux boot environment."],
            Problems());
        Assert.Equal((ConsoleStage.Stopped, ConsoleRemedy.Restart), (console.States[^1].Stage, console.States[^1].Problem?.Remedy));
    }

    // An agent put together without a firmware, as in a test: it stays, and says so once however often it is told.
    [Fact]
    public async Task AnAgentThatCannotChangeTheEnvironmentStaysAndSaysSoOnce()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered())
            .OnNext(_ => Next("session-1", "resume-1", restartInto: AgentEnvironment.LinuxBoot))
            .OnNext(_ => Next("session-2", "resume-2", restartInto: AgentEnvironment.LinuxBoot));

        int exitCode = await UnattendedLoop(server, change: _ => null).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Empty(_firmware.Calls);
        Assert.Equal(["WARN  This agent cannot restart the machine into the Linux boot environment."], Problems());
    }
}
