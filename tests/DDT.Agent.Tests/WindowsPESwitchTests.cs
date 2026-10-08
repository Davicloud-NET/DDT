// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using Xunit;

namespace DDT.Agent.Tests;

// The agent on its way out of Windows PE into the other boot environment, with the rebooter Windows PE has. wpeutil
// takes the machine down at once, so the server must have the log's last lines by the time it runs.
public sealed class WindowsPESwitchTests : IDisposable
{
    private const string Restarts = "The machine restarts into the Linux boot environment, where it stays signed in and goes on.";
    private const string NextStart = "The next start comes from boot entry Boot0003 again, the one this start came from.";

    private static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");

    private static readonly AgentSequenceChoice s_linux = new(
        Guid.Parse("0193a4b2-0000-7000-8000-00000000e004"),
        "Install Ubuntu",
        null,
        ErasesDisk: true,
        NeedsComputerName: true,
        RequiredBytes: 0,
        Suggested: false,
        Environment: AgentEnvironment.LinuxBoot);

    private readonly FakeDeploymentTools _tools = new();
    private readonly FakeVendorVariables _vendor = new();
    private readonly FakeUefiVariables _firmware = new();
    private readonly RecordingToolRunner _wpeutil = new();

    // What the server had been sent, and had been asked, when wpeutil ran.
    private List<AgentLogLine> _sentAtRestart = [];
    private List<string> _callsAtRestart = [];

    public WindowsPESwitchTests()
    {
        _firmware.Values["BootCurrent"] = [0x03, 0x00];
        _firmware.Values["Boot0003"] = [0x01];
    }

    public void Dispose() => _tools.Dispose();

    private static AgentRegistrationResult Registered() => new(s_machineId, MachineState.Approved, "session-0", "resume-0", 10, "bob");

    private static ScriptedAgentServer Ordered() => new ScriptedAgentServer()
        .OnRegister(_ => Registered())
        .OnNext(_ => new AgentNextResult(MachineState.Approved, "session-1", "resume-1", 10, "bob", RestartInto: AgentEnvironment.LinuxBoot));

    private AgentLoop Loop(ScriptedAgentServer server, IMachineConsole? console = null)
    {
        ImmediateTimeProvider time = new();
        AgentLog log = new(time, TextWriter.Null);
        _wpeutil.Answer = (_, _) =>
        {
            _sentAtRestart = [.. server.SentLines];
            _callsAtRestart = server.Calls;

            return [];
        };
        TestMachine machine = new(_tools, log, time)
        {
            Switch = new EnvironmentSwitch(new ResumeCarrier(_vendor, AgentEnvironment.WindowsPE, log), new WindowsPERebooter(_wpeutil, _firmware, log)),
        };

        return TestAgents.Loop(server, TestAgents.Status(console ?? new TextMachineConsole(new ScriptedSignInPrompt { IsAvailable = false }, log)), machine);
    }

    [Fact]
    public async Task TheServerHasTheLastLinesOfARestartItOrderedBeforeWindowsPEGoesDown()
    {
        ScriptedAgentServer server = Ordered();

        int exitCode = await Loop(server).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Restarting, exitCode);
        Assert.Single(_wpeutil.Calls);
        Assert.Equal([Restarts, NextStart], _sentAtRestart.Select(line => line.Message).TakeLast(2));
    }

    [Fact]
    public async Task TheServerHasTheLastLinesOfASequencePickedAtTheMachineBeforeWindowsPEGoesDown()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered())
            .OnNext(_ => new AgentNextResult(MachineState.Approved, "session-1", "resume-1", 10, "bob", CanPickSequence: true))
            .OnSequences(() => [s_linux])
            .OnEnvironment(_ => { });
        ScriptedMachineConsole console = new(ScriptedMachineConsole.Sequence("Install Ubuntu"));

        int exitCode = await Loop(server, console).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Restarting, exitCode);
        Assert.Single(_wpeutil.Calls);
        Assert.Equal([Restarts, NextStart], _sentAtRestart.Select(line => line.Message).TakeLast(2));
    }

    // The one line that says why someone has to sign in again at a machine that came back as a stranger.
    [Fact]
    public async Task TheServerHasTheWarningOfAFirmwareThatDidNotKeepTheTokenBeforeWindowsPEGoesDown()
    {
        _vendor.WriteFailure = "The firmware variable DdtResume cannot be written (Windows error 1314).";
        ScriptedAgentServer server = Ordered();

        int exitCode = await Loop(server).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Restarting, exitCode);
        Assert.StartsWith(
            "The firmware did not keep what lets this machine stay signed in across the restart (",
            Assert.Single(_sentAtRestart, line => line.Level == AgentLogLevel.Warning).Message,
            StringComparison.Ordinal);
    }

    // The client gives a request its time and no more. The lines get one try, and then the machine restarts.
    [Fact]
    public async Task AServerThatDoesNotAnswerGetsOneTryAndDoesNotKeepWindowsPEFromRestarting()
    {
        ScriptedAgentServer server = Ordered();
        server.AnswerLogs = _ => throw new ServerTimeoutException("the server did not answer within 30 seconds");

        int exitCode = await Loop(server).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Restarting, exitCode);
        Assert.Single(_wpeutil.Calls);
        Assert.Equal(["register", "next session-0", "log session-1"], _callsAtRestart);
        Assert.Empty(_sentAtRestart);
    }

    // As before: the machine goes down, and the loop that registers again goes down with it.
    [Fact]
    public async Task ATokenTheServerRefusesWithTheLastLinesDoesNotKeepWindowsPEFromRestarting()
    {
        ScriptedAgentServer server = Ordered();
        server.AnswerLogs = _ => throw new AgentTokenRejectedException();

        await Loop(server).RunAsync(server.Stop.Token);

        Assert.Single(_wpeutil.Calls);
        Assert.Equal(ResumeCarrier.Encode(AgentEnvironment.LinuxBoot, "resume-1"), _vendor.Resume);
    }
}
