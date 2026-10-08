// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

// The restart of the Linux boot environment: where the next start leads, and when the machine really restarts.
public sealed class LinuxRestartTests : IDisposable
{
    private readonly LinuxTree _tree = new();
    private readonly StringWriter _console = new();
    private readonly List<string> _started = [];
    private readonly LinuxUefiVariables _variables;
    private int _kernelRestarts;

    public LinuxRestartTests()
    {
        Directory.CreateDirectory(_tree.Variables);
        _variables = new LinuxUefiVariables(_tree.Root, _ => { });
    }

    public void Dispose() => _tree.Dispose();

    private AgentLog Log() => new(new ImmediateTimeProvider(), _console);

    // A kernel that answers with error 1, as it does to a process that may not restart the machine.
    private LinuxRestart Restart(bool toolStarts = true) => new(
        Log(),
        () =>
        {
            _kernelRestarts++;

            return 1;
        },
        tool =>
        {
            _started.Add(tool);

            return toolStarts;
        });

    [Fact]
    public async Task BackIntoTheBootEnvironmentTheNextStartComesFromThisStartsEntry()
    {
        _tree.WriteVariable("BootCurrent", LinuxUefiVariables.Global, 0x04, 0x00);
        _tree.WriteVariable("Boot0004", LinuxUefiVariables.Global, 1, 0, 0, 0, 0, 0, (byte)'N', 0, 0, 0);
        LinuxRestart restart = Restart();

        await new LinuxBootRebooter(_variables, restart, Log()).RebootAsync(RestartInto.BootEnvironment, TestContext.Current.CancellationToken);

        Assert.Equal([0x07, 0x00, 0x00, 0x00, 0x04, 0x00], _tree.ReadVariableFile("BootNext", LinuxUefiVariables.Global));
        Assert.True(restart.Issued);
        Assert.Contains("The next start comes from boot entry Boot0004 again, the one this start came from.", _console.ToString(), StringComparison.Ordinal);
    }

    // The run put the disk first, so the firmware's own order leads there.
    [Fact]
    public async Task IntoTheInstalledSystemTheRestartIsPlain()
    {
        _tree.WriteVariable("BootCurrent", LinuxUefiVariables.Global, 0x04, 0x00);
        LinuxRestart restart = Restart();

        await new LinuxBootRebooter(_variables, restart, Log()).RebootAsync(RestartInto.InstalledSystem, TestContext.Current.CancellationToken);

        Assert.Null(_variables.Read("BootNext"));
        Assert.True(restart.Issued);
    }

    [Fact]
    public async Task WithoutAnEntryToComeBackToTheRestartIsPlainAndSaysWhereItMayLead()
    {
        LinuxRestart restart = Restart();

        await new LinuxBootRebooter(_variables, restart, Log()).RebootAsync(RestartInto.BootEnvironment, TestContext.Current.CancellationToken);

        Assert.True(restart.Issued);
        Assert.Contains(
            "The firmware does not say which boot entry this start came from. The machine restarts without choosing where it starts " +
            "next, so it comes back to the Linux boot environment only if its boot order starts from the network before the disk.",
            _console.ToString(),
            StringComparison.Ordinal);
    }

    // The agent still has lines for the server when a run issues the restart. Nothing restarts until it has ended.
    [Fact]
    public async Task TheMachineRestartsOnlyWhenTheAgentEnds()
    {
        LinuxRestart restart = Restart();

        restart.NowIfIssued();
        Assert.Equal(0, _kernelRestarts);

        await new LinuxBootRebooter(_variables, restart, Log()).RebootAsync(RestartInto.InstalledSystem, TestContext.Current.CancellationToken);
        Assert.Equal(0, _kernelRestarts);

        restart.NowIfIssued();
        Assert.Equal(1, _kernelRestarts);
    }

    [Fact]
    public void AsksTheSystemsOwnToolWhenTheKernelDoesNotRestart()
    {
        LinuxRestart restart = Restart();
        restart.Issue();

        restart.NowIfIssued();

        Assert.Equal(["reboot"], _started);
        Assert.Contains("The kernel did not restart the machine (error 1), so reboot -f was started for it.", _console.ToString(), StringComparison.Ordinal);
    }

    // The agent's exit code then tells the start script, which restarts the machine as its last net.
    [Fact]
    public void SaysSoWhenNothingRestartsTheMachine()
    {
        LinuxRestart restart = Restart(toolStarts: false);
        restart.Issue();

        restart.NowIfIssued();

        Assert.Contains("The machine could not restart itself (error 1, and reboot cannot be started).", _console.ToString(), StringComparison.Ordinal);
    }
}
