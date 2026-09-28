// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class OfflineServiceRegistrationTests
{
    private const string Windows = @"W:\";
    private const string Service = @"HKLM\DDT_OFFLINE\ControlSet002\Services\DdtSequence";

    private static readonly string s_reg = OfflineServiceRegistration.RegPath;

    private readonly RecordingToolRunner _tools = new();
    private readonly StringWriter _console = new();
    private readonly AgentLog _log;

    public OfflineServiceRegistrationTests()
    {
        _log = new AgentLog(new ImmediateTimeProvider(), _console);
        _tools.Answer = (_, arguments) => arguments[0] == "query"
            ? [string.Empty, @"HKEY_LOCAL_MACHINE\DDT_OFFLINE\Select", "    Current    REG_DWORD    0x2", string.Empty]
            : [];
    }

    [Fact]
    public async Task RegistersTheServiceInTheCurrentControlSetOfTheOfflineHive()
    {
        await new OfflineServiceRegistration(_tools, _log, dryRun: false).RegisterAsync(Windows, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                Reg("load", @"HKLM\DDT_OFFLINE", @"W:\Windows\System32\config\SYSTEM"),
                Reg("query", @"HKLM\DDT_OFFLINE\Select", "/v", "Current"),
                Add("Type", "REG_DWORD", "16"),
                Add("Start", "REG_DWORD", "2"),
                Add("ErrorControl", "REG_DWORD", "1"),
                Add("ImagePath", "REG_EXPAND_SZ", "\"%SystemDrive%\\DDT\\agent\\ddt-agent.exe\" --service"),
                Add("ObjectName", "REG_SZ", "LocalSystem"),
                Add("DisplayName", "REG_SZ", "DDT task sequence"),
                Add("Description", "REG_SZ", OfflineServiceRegistration.Description),
                Add("FailureActions", "REG_BINARY", OfflineServiceRegistration.FailureActions),
                Reg("unload", @"HKLM\DDT_OFFLINE"),
            ],
            _tools.Calls);
    }

    [Fact]
    public void RestartsTheServiceAfterAMinuteAtEachOfThreeFailures()
    {
        byte[] actions = Convert.FromHexString(OfflineServiceRegistration.FailureActions);

        Assert.Equal(20 + (3 * 8), actions.Length);
        Assert.Equal(86400, BinaryPrimitives.ReadInt32LittleEndian(actions));
        Assert.Equal([0, 0, 3, 20], [.. Enumerable.Range(1, 4).Select(field => BinaryPrimitives.ReadInt32LittleEndian(actions.AsSpan(field * 4)))]);

        for (int action = 0; action < 3; action++)
        {
            // SC_ACTION_RESTART, after 60 000 ms.
            Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(actions.AsSpan(20 + (action * 8))));
            Assert.Equal(60_000, BinaryPrimitives.ReadInt32LittleEndian(actions.AsSpan(24 + (action * 8))));
        }
    }

    [Fact]
    public async Task UnloadsTheHiveWhenTheRegistrationFails()
    {
        _tools.Answer = (_, arguments) => arguments[0] switch
        {
            "query" => ["    Current    REG_DWORD    0x1"],
            "add" => throw new DeploymentStepException("reg.exe failed with exit code 0x00000001."),
            "unload" => throw new DeploymentStepException("reg.exe failed with exit code 0x00000005."),
            _ => [],
        };

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(
            () => new OfflineServiceRegistration(_tools, _log, dryRun: false).RegisterAsync(Windows, TestContext.Current.CancellationToken));

        // The first failure is the one reported. The unload's failure only logs a warning.
        Assert.Equal("reg.exe failed with exit code 0x00000001.", exception.Message);
        Assert.Equal(Reg("unload", @"HKLM\DDT_OFFLINE"), _tools.Calls[^1]);
        Assert.Contains("could not be unloaded (reg.exe failed with exit code 0x00000005.)", _console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailsWhenTheHiveCannotBeUnloaded()
    {
        _tools.Answer = (_, arguments) => arguments[0] switch
        {
            "query" => ["    Current    REG_DWORD    0x1"],
            "unload" => throw new DeploymentStepException("reg.exe failed with exit code 0x00000005."),
            _ => [],
        };

        await Assert.ThrowsAsync<DeploymentStepException>(
            () => new OfflineServiceRegistration(_tools, _log, dryRun: false).RegisterAsync(Windows, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RefusesAHiveThatDoesNotSayWhichControlSetIsCurrent()
    {
        _tools.Answer = (_, _) => [];

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(
            () => new OfflineServiceRegistration(_tools, _log, dryRun: false).RegisterAsync(Windows, TestContext.Current.CancellationToken));

        Assert.StartsWith("The installed Windows' SYSTEM hive does not say which control set is current", exception.Message, StringComparison.Ordinal);
        Assert.Equal([Reg("load", @"HKLM\DDT_OFFLINE", @"W:\Windows\System32\config\SYSTEM"), Reg("query", @"HKLM\DDT_OFFLINE\Select", "/v", "Current"), Reg("unload", @"HKLM\DDT_OFFLINE")], _tools.Calls);
    }

    [Fact]
    public async Task ADryRunTakesTheFirstControlSet()
    {
        _tools.Answer = (_, _) => [];

        await new OfflineServiceRegistration(_tools, _log, dryRun: true).RegisterAsync(Windows, TestContext.Current.CancellationToken);

        Assert.Contains(_tools.Calls, call => call.StartsWith(Reg("add", @"HKLM\DDT_OFFLINE\ControlSet001\Services\DdtSequence"), StringComparison.Ordinal));
        Assert.Contains("ControlSet001 stands in for it", _console.ToString(), StringComparison.Ordinal);
    }

    private static string Reg(params string[] arguments) => RecordingToolRunner.CommandLine(s_reg, arguments);

    private static string Add(string name, string type, string data) => Reg("add", Service, "/v", name, "/t", type, "/d", data, "/f");
}
