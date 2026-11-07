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
        _variables = _tree.Firmware();
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
        Assert.Equal("", _console.ToString());
    }

    // A restart into the other boot environment that never happened left the network as the next start.
    [Fact]
    public async Task IntoTheInstalledSystemANextStartSetEarlierIsTakenBack()
    {
        _tree.WriteVariable("BootNext", LinuxUefiVariables.Global, 0x04, 0x00);

        await new LinuxBootRebooter(_variables, Restart(), Log()).RebootAsync(RestartInto.InstalledSystem, TestContext.Current.CancellationToken);

        Assert.Null(_variables.Read("BootNext"));
        Assert.Contains(
            "The firmware was still to start once from the entry an earlier restart had named. That is taken back, so the next start follows the boot order.",
            _console.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANextStartThatCannotBeTakenBackIsSaidAndTheRestartIsIssuedAnyway()
    {
        _tree.WriteVariable("BootNext", LinuxUefiVariables.Global, 0x04, 0x00);
        LinuxUefiVariables refusing = new(_tree.Root, _ => throw new IOException("Operation not permitted"), _tree.Efivarfs.Open);
        LinuxRestart restart = Restart();

        await new LinuxBootRebooter(refusing, restart, Log()).RebootAsync(RestartInto.InstalledSystem, TestContext.Current.CancellationToken);

        Assert.True(restart.Issued);
        Assert.Contains(
            "The firmware is still to start once from the entry an earlier restart had named, and that could not be taken back " +
            "(The firmware variable BootNext cannot be deleted (Operation not permitted).). The start after that one follows the boot order.",
            _console.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWithdrawnRestartDoesNotHappenAndLeavesNothingInTheFirmware()
    {
        _tree.WriteVariable("BootCurrent", LinuxUefiVariables.Global, 0x04, 0x00);
        _tree.WriteVariable("Boot0004", LinuxUefiVariables.Global, 1, 0, 0, 0, 0, 0, (byte)'N', 0, 0, 0);
        LinuxRestart restart = Restart();
        LinuxBootRebooter rebooter = new(_variables, restart, Log());
        ResumeCarrier carrier = new(_variables, DDT.Contracts.Agents.AgentEnvironment.LinuxBoot, Log());
        carrier.Put(DDT.Contracts.Agents.AgentEnvironment.WindowsPE, "resume-1");
        await rebooter.RebootAsync(RestartInto.BootEnvironment, TestContext.Current.CancellationToken);

        rebooter.Withdraw(carrier);
        restart.NowIfIssued();

        Assert.Equal(0, _kernelRestarts);
        Assert.Null(_variables.Read("BootNext"));
        Assert.Null(_variables.Read(ResumeCarrier.VariableName, ResumeCarrier.Vendor));
        Assert.Contains("The agent goes on, so the restart it had issued does not happen.", _console.ToString(), StringComparison.Ordinal);
    }

    // Nothing was issued, so nothing is taken back: a token another agent left is not this restart's.
    [Fact]
    public void WithdrawingWithoutARestartChangesNothing()
    {
        _tree.WriteVariable("BootNext", LinuxUefiVariables.Global, 0x04, 0x00);
        ResumeCarrier carrier = new(_variables, DDT.Contracts.Agents.AgentEnvironment.LinuxBoot, Log());
        carrier.Put(DDT.Contracts.Agents.AgentEnvironment.WindowsPE, "resume-1");

        new LinuxBootRebooter(_variables, Restart(), Log()).Withdraw(carrier);

        Assert.Equal([0x04, 0x00], _variables.Read("BootNext"));
        Assert.NotNull(_variables.Read(ResumeCarrier.VariableName, ResumeCarrier.Vendor));
        Assert.Equal("", _console.ToString());
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

    // The kernel restarts without unmounting anything, so what holds the disks lets go first.
    [Fact]
    public void LetsGoOfTheDisksBeforeTheKernelRestartsAndRestartsAlsoWhenThatFails()
    {
        List<string> order = [];
        LinuxRestart restart = new(Log(), () =>
        {
            order.Add("kernel");

            return 1;
        }, _ => true)
        {
            Before = () =>
            {
                order.Add("before");

                throw new InvalidOperationException("The list of mounts is gone.");
            },
        };
        restart.Issue();

        restart.NowIfIssued();

        Assert.Equal(["before", "kernel"], order);
        Assert.Contains("The disks could not be let go of before the restart (The list of mounts is gone.).", _console.ToString(), StringComparison.Ordinal);
    }
}
