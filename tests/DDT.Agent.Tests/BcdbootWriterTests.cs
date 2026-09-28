// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class BcdbootWriterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ddt-bcdboot-").FullName;
    private readonly RecordingToolRunner _tools = new();
    private readonly FakeUefiVariables _variables = new();
    private readonly AgentLog _log = new(new ImmediateTimeProvider(), TextWriter.Null);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // Drive roots like S:\, the way the partitioning passes them on.
    private TargetVolumes Volumes => new($@"{_root}\S\", $@"{_root}\W\", $@"{_root}\R\", []);

    private string Windows => Path.Combine(_root, "W", "Windows");

    private string System32 => Path.Combine(Windows, "System32");

    private string Bcdboot => RecordingToolRunner.CommandLine(Path.Combine(System32, "bcdboot.exe"), Windows, "/s", Path.Combine(_root, "S"), "/f", "UEFI");

    [Fact]
    public async Task MakesTheDiskBootableAndRegistersTheRecoveryEnvironment()
    {
        byte[] winre = [1, 2, 3, 4];
        Directory.CreateDirectory(Path.Combine(System32, "Recovery"));
        await File.WriteAllBytesAsync(Path.Combine(System32, "Recovery", "Winre.wim"), winre, TestContext.Current.CancellationToken);
        string recovery = Path.Combine(_root, "R", "Recovery", "WindowsRE");

        await new BcdbootWriter(_tools, _variables, _log).WriteAsync(Volumes, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                Bcdboot,
                RecordingToolRunner.CommandLine(Path.Combine(System32, "reagentc.exe"), "/setreimage", "/path", recovery, "/target", Windows),
            ],
            _tools.Calls);
        Assert.Equal(winre, await File.ReadAllBytesAsync(Path.Combine(recovery, "Winre.wim"), TestContext.Current.CancellationToken));
        Assert.Empty(_variables.Writes);
    }

    [Fact]
    public async Task SkipsTheRecoveryEnvironmentWhenTheImageHasNone()
    {
        Directory.CreateDirectory(System32);

        await new BcdbootWriter(_tools, _variables, _log).WriteAsync(Volumes, TestContext.Current.CancellationToken);

        Assert.Equal([Bcdboot], _tools.Calls);
        Assert.False(Directory.Exists(Path.Combine(_root, "R", "Recovery")));
        Assert.Contains(await SentAsync(), line => line.Level == AgentLogLevel.Warning && line.Message.EndsWith(
            "is missing, so this Windows gets no recovery environment. Deploy an image that contains it to have one.",
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task PuttingWindowsFirstOnlyWarnsWhenItFails()
    {
        _tools.Answer = (_, _) => throw new DeploymentStepException("bcdedit.exe failed with exit code 0x00000001. Its output is in the machine log.");

        // The system volume is a directory here, so the partition behind it cannot be read.
        await new BcdbootWriter(_tools, _variables, _log).PutWindowsFirstAsync(Volumes, TestContext.Current.CancellationToken);

        Assert.Equal([RecordingToolRunner.CommandLine(Path.Combine(Environment.SystemDirectory, "bcdedit.exe"), "/enum", "firmware")], _tools.Calls);
        Assert.Empty(_variables.Writes);

        List<AgentLogLine> lines = await SentAsync();
        Assert.Contains(lines, line => line.Level == AgentLogLevel.Warning && line.Message.StartsWith(
            "Windows Boot Manager could not be put first in the firmware boot order (",
            StringComparison.Ordinal) && line.Message.EndsWith(
            "so this machine may start from the network again. Set its boot order to start Windows Boot Manager first.",
            StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Level == AgentLogLevel.Warning && line.Message.StartsWith(
            "The firmware boot entries cannot be listed (",
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task PutsNothingBackWhenPuttingWindowsFirstChangedNothing()
    {
        BcdbootWriter writer = new(_tools, _variables, _log);

        // The system volume is a directory here, so no firmware variable is written.
        await writer.PutWindowsFirstAsync(Volumes, TestContext.Current.CancellationToken);
        await writer.RestoreBootOrderAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_variables.Writes);
        Assert.Empty(_variables.Deletes);
        Assert.DoesNotContain(await SentAsync(), line => line.Message.StartsWith("The firmware boot order", StringComparison.Ordinal));
    }

    private async Task<List<AgentLogLine>> SentAsync()
    {
        ScriptedAgentServer server = new();

        while (_log.QueuedLines > 0)
        {
            await _log.FlushAsync(server, Guid.Empty, "token", TestContext.Current.CancellationToken);
        }

        return server.SentLines;
    }
}
