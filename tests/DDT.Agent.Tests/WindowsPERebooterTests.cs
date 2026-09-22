// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class WindowsPERebooterTests
{
    private static readonly string s_wpeutil = RecordingToolRunner.CommandLine(Path.Combine(Environment.SystemDirectory, "wpeutil.exe"), "reboot");

    private readonly FakeUefiVariables _variables = new();
    private readonly RecordingToolRunner _tools = new();
    private readonly AgentLog _log = new(new ImmediateTimeProvider(), TextWriter.Null);
    private bool _bootNextBeforeRestart;

    public WindowsPERebooterTests()
    {
        _tools.Answer = (_, _) =>
        {
            _bootNextBeforeRestart = _variables.Values.ContainsKey("BootNext");

            return [];
        };
    }

    [Fact]
    public async Task StartsNextFromTheEntryThisStartCameFrom()
    {
        _variables.Values["BootCurrent"] = [0x03, 0x00];
        _variables.Values["Boot0003"] = [0x01];

        await Rebooter().RebootAsync(RestartInto.WindowsPE, TestContext.Current.CancellationToken);

        Assert.Equal(["BootNext"], _variables.Writes);
        Assert.Equal([0x03, 0x00], _variables.Values["BootNext"]);
        Assert.Equal([s_wpeutil], _tools.Calls);
        Assert.True(_bootNextBeforeRestart);
        Assert.Contains(await SentAsync(), line => line.Message == "The next start comes from boot entry Boot0003 again, the one this start came from.");
    }

    [Fact]
    public async Task RestartsPlainlyWhenThatEntryIsGone()
    {
        _variables.Values["BootCurrent"] = [0x0A, 0x00];

        await Rebooter().RebootAsync(RestartInto.WindowsPE, TestContext.Current.CancellationToken);

        Assert.Empty(_variables.Writes);
        Assert.Equal([s_wpeutil], _tools.Calls);
        Assert.Contains(
            await SentAsync(),
            line => line.Level == AgentLogLevel.Warning && line.Message.StartsWith("The boot entry this start came from, Boot000A, is gone", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestartsPlainlyWhenTheFirmwareDoesNotSayWhereThisStartCameFrom()
    {
        await Rebooter().RebootAsync(RestartInto.WindowsPE, TestContext.Current.CancellationToken);

        Assert.Empty(_variables.Writes);
        Assert.Equal([s_wpeutil], _tools.Calls);
        Assert.Contains(await SentAsync(), line => line.Level == AgentLogLevel.Warning);
    }

    [Fact]
    public async Task RestartsEvenWhenTheFirmwareCannotBeRead()
    {
        _variables.Failure = new DeploymentStepException("This machine did not start in UEFI mode, so its firmware variables cannot be read.");

        await Rebooter().RebootAsync(RestartInto.WindowsPE, TestContext.Current.CancellationToken);

        Assert.Equal([s_wpeutil], _tools.Calls);
        Assert.Contains(
            await SentAsync(),
            line => line.Level == AgentLogLevel.Warning && line.Message.StartsWith("The next start could not be set", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IntoWindowsItLeavesTheFirmwareAlone()
    {
        _variables.Values["BootCurrent"] = [0x03, 0x00];
        _variables.Values["Boot0003"] = [0x01];

        await Rebooter().RebootAsync(RestartInto.Windows, TestContext.Current.CancellationToken);

        Assert.Empty(_variables.Writes);
        Assert.Equal([s_wpeutil], _tools.Calls);
    }

    [Theory]
    [InlineData(RestartInto.WindowsPE, "BootNext would be set to BootCurrent")]
    [InlineData(RestartInto.Windows, "In Windows PE, wpeutil reboot would run now.")]
    public async Task ADryRunOnlySaysWhatWouldHappen(RestartInto into, string said)
    {
        await new DryRunRebooter(_log).RebootAsync(into, TestContext.Current.CancellationToken);

        Assert.Contains(said, Assert.Single(await SentAsync()).Message, StringComparison.Ordinal);
    }

    private WindowsPERebooter Rebooter() => new(_tools, _variables, _log);

    private async Task<List<AgentLogLine>> SentAsync()
    {
        ScriptedAgentServer server = new();

        while (_log.QueuedLines > 0)
        {
            await _log.FlushAsync(server, Guid.Empty, "session", TestContext.Current.CancellationToken);
        }

        return server.SentLines;
    }
}
