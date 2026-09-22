// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.WindowsPhase;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class WindowsRebooterTests
{
    [Fact]
    public async Task RestartsWindowsAtOnceAsAPlannedRestartWithItsReason()
    {
        RecordingToolRunner tools = new();

        await new WindowsRebooter(tools).RebootAsync(RestartInto.Windows, TestContext.Current.CancellationToken);

        Assert.Equal(
            [RecordingToolRunner.CommandLine(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), "/r", "/t", "0", "/d", "p:4:1", "/c", "DDT restarts Windows for its task sequence.")],
            tools.Calls);
    }

    // A dry run restarts by ending its process or starting over within it, so no restart it asked for is ever due.
    [Fact]
    public void ADryRunOnlySaysWhereTheRestartWouldBeRecorded()
    {
        StringWriter console = new();
        DryRunRestartMarker marker = new(new AgentLog(new ImmediateTimeProvider(), console));

        marker.Set();

        Assert.False(marker.IsSet);
        Assert.Contains(@"HKLM\SYSTEM\CurrentControlSet\Services\DdtSequence\RestartDue", console.ToString(), StringComparison.Ordinal);
    }
}
