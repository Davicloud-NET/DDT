// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using Xunit;

namespace DDT.Agent.Tests;

// The way back to the disk as the Linux boot environment wires it, for a machine that started from the network while
// its run was handed over: the firmware through efivarfs, and the restart once the agent has ended.
public sealed class LinuxReturnToDiskTests : IDisposable
{
    private static readonly EspPartition s_esp = new(TestRawImage.EspNumber, TestRawImage.EspFirst, TestRawImage.EspSectors, TestRawImage.EspId);

    private readonly LinuxTree _tree = new();
    private readonly SeededRun _seeded = new();
    private readonly StringWriter _console = new();
    private readonly ImmediateTimeProvider _time = new();
    private readonly AgentLog _log;
    private readonly LinuxUefiVariables _firmware;
    private readonly LinuxRestart _restart;
    private readonly List<string> _atRestart = [];
    private int _callsBefore;
    private int _letGo;

    public LinuxReturnToDiskTests()
    {
        Directory.CreateDirectory(_tree.Variables);
        _log = new AgentLog(_time, _console);
        _firmware = _tree.Firmware();

        // A firmware that put the network first again, after the hand-over had put the disk's entry first.
        _tree.WriteVariable("Boot0001", LinuxUefiVariables.Global, 1, 0, 0, 0, 0, 0, (byte)'N', 0, 0, 0);
        _tree.WriteVariable("Boot0002", LinuxUefiVariables.Global, EfiLoadOption.Build("noble-test", s_esp, FirmwareBootEntry.FallbackLoaderPath));
        _tree.WriteVariable("BootOrder", LinuxUefiVariables.Global, 0x01, 0x00, 0x02, 0x00);
        _tree.WriteVariable("BootCurrent", LinuxUefiVariables.Global, 0x01, 0x00);

        // A kernel that refuses, as it does a process that may not restart the machine, so the test goes on after it.
        _restart = new LinuxRestart(
            new AgentLog(_time, _console),
            () =>
            {
                _atRestart.AddRange(Server.Calls.Skip(_callsBefore));
                _atRestart.Add("kernel restart");

                return 1;
            },
            _ => true)
        {
            Before = () => _letGo++,
        };
    }

    public void Dispose()
    {
        _seeded.Dispose();
        _tree.Dispose();
        _console.Dispose();
    }

    private ScriptedAgentServer Server => _seeded.Run.Server;

    // The look and what follows it, put together as LinuxBoot does, with the tests' server, disks and keyboard. With
    // startOver someone presses a key and types the word.
    private Task<int> RunAsync(bool startOver = false)
    {
        _callsBefore = Server.Calls.Count;
        LinuxBootRebooter rebooter = new(_firmware, _restart, _log);
        AgentMachine machine = new(new DryRunMachineIdentityReader(1), _seeded.Run.Tools, new LocalRunLocator([]), TestAgents.Version, AgentEnvironment.LinuxBoot);
        ConsoleStatus status = TestAgents.Status(new ScriptedSignInPrompt { IsAvailable = false }, new AgentLog(_time, TextWriter.Null));
        LinuxBootReturn returns = new(Server, machine, status, _log, _time)
        {
            Seeds = new HandedOverSeeds(_seeded.Run.RawDisks, _log, _time),
            StartOver = new StartOverQuestion(new FakeKeyWait { Pressed = startOver }, new ScriptedSignInPrompt(StartOverQuestion.Word), _log, _time),
            DiskStart = new FirmwareDiskStart(_firmware, _log),
            Restart = rebooter.RestartIntoDiskAsync,
        };

        return LinuxBoot.RunAsync(async stop => (await returns.RunAsync(stop)).ExitCode ?? -1, _restart, Server.Stop.Token);
    }

    // The installed Linux may have put an entry of its own first, so the order is left to it.
    [Fact]
    public async Task ARunTheInstalledLinuxHasSendsTheMachineBackThroughBootNextAlone()
    {
        await _seeded.HandedOverAsync();
        _seeded.WipeToken();
        Server.OnRegister(_ => BootReturnStart.GoesOn());

        int exitCode = await RunAsync();

        Assert.Equal(AgentExitCodes.Restarting, exitCode);
        Assert.Equal([0x02, 0x00], _firmware.Read("BootNext"));
        Assert.Equal([0x01, 0x00, 0x02, 0x00], _firmware.Read("BootOrder"));
        Assert.Equal(["register", "kernel restart"], _atRestart);
        Assert.True(_letGo > 0);
        Assert.Contains("The next start comes from boot entry Boot0002, which starts disk 0, whatever the boot order says.", _console.ToString(), StringComparison.Ordinal);
    }

    // The server has the last lines before the kernel restarts, which say what the firmware was told.
    [Fact]
    public async Task ARunThatWasNotTakenOverGetsItsDiskFirstAgainAndTheRestartComesLast()
    {
        await _seeded.HandedOverAsync();
        Server.OnRegister(_ => new AgentRegistrationResult(StepRunnerFixture.MachineId, MachineState.Deploying, "session-0", "resume-0", 10, null, StepRunnerFixture.RunId, "run-token-2"));
        Server.AnswerRunReports = (_, _) => new AgentRunReportResult("session-1", "resume-1", "run-token-3");

        int exitCode = await RunAsync();

        Assert.Equal(AgentExitCodes.Restarting, exitCode);
        Assert.Equal([0x02, 0x00], _firmware.Read("BootNext"));
        Assert.Equal([0x02, 0x00, 0x01, 0x00], _firmware.Read("BootOrder"));
        Assert.Equal(["register", "run-report Running session-0", "log session-1", "log session-1", "kernel restart"], _atRestart);
        Assert.Contains(Server.SentLines, line => line.Message == "The next start comes from boot entry Boot0002, which starts disk 0, whatever the boot order says.");
    }

    // Any other restart into the installed system names the first entry of the boot order, whatever was set before it.
    [Fact]
    public async Task TheRestartIntoTheDiskKeepsTheNextStartThatWasSetForIt()
    {
        LinuxBootRebooter rebooter = new(_firmware, _restart, _log);
        new FirmwareDiskStart(_firmware, _log).Next(FakeDeploymentTools.Disk(0), s_esp, first: false);

        await rebooter.RestartIntoDiskAsync();

        Assert.True(_restart.Issued);
        Assert.Equal(1, _letGo);
        Assert.Equal([0x02, 0x00], _firmware.Read("BootNext"));

        await rebooter.RebootAsync(RestartInto.InstalledSystem, TestContext.Current.CancellationToken);

        Assert.Equal([0x01, 0x00], _firmware.Read("BootNext"));
    }

    [Fact]
    public async Task AStartOverLeavesTheFirmwareAsItWasAndRestartsNothing()
    {
        await _seeded.HandedOverAsync();
        _seeded.WipeToken();
        Server.OnRegister(_ => BootReturnStart.GoesOn()).OnRegister(_ => BootReturnStart.StartedOver());

        int exitCode = await RunAsync(startOver: true);

        Assert.Equal(-1, exitCode);
        Assert.Null(_firmware.Read("BootNext"));
        Assert.Equal([0x01, 0x00, 0x02, 0x00], _firmware.Read("BootOrder"));
        Assert.Empty(_atRestart);
        Assert.False(_restart.Issued);
    }
}
