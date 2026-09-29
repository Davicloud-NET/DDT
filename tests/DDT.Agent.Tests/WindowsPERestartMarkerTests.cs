// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

// The marker in a temporary folder that stands in for X:\DDT. WinPE's restart empties that folder, and so does Dispose.
public sealed class WindowsPERestartMarkerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ddt-restart-marker-{Guid.NewGuid():N}");
    private readonly StringWriter _console = new();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private AgentLog Log() => new(new ImmediateTimeProvider(), _console);

    [Fact]
    public void KeepsWhereTheDueRestartLeadsUntilTheRunGivesItUp()
    {
        WindowsPERestartMarker marker = new(_directory, Log(), dryRun: false);

        Assert.Null(marker.Due);

        marker.Set(RestartInto.WindowsPE);

        // Another agent in the same WinPE, like one started by hand, finds it.
        Assert.Equal(RestartInto.WindowsPE, new WindowsPERestartMarker(_directory, Log(), dryRun: false).Due);

        marker.Set(RestartInto.Windows);

        Assert.Equal(RestartInto.Windows, marker.Due);

        marker.Clear();

        Assert.Null(marker.Due);
        Assert.False(File.Exists(marker.FilePath));
        Assert.Empty(_console.ToString());
    }

    // The file existing is what says a restart is due. Its text only says where to.
    [Fact]
    public async Task AMarkerItCannotMakeSenseOfLeadsBackIntoWindowsPE()
    {
        WindowsPERestartMarker marker = new(_directory, Log(), dryRun: false);
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(marker.FilePath, "Wind", TestContext.Current.CancellationToken);

        Assert.Equal(RestartInto.WindowsPE, marker.Due);
    }

    [Fact]
    public void AMarkerThatCannotBeWrittenOnlyWarns()
    {
        // A file where the directory should be.
        File.WriteAllText(_directory, string.Empty);

        try
        {
            WindowsPERestartMarker marker = new(_directory, Log(), dryRun: false);

            marker.Set(RestartInto.WindowsPE);

            Assert.Null(marker.Due);
            Assert.Contains("WARN  The restart could not be recorded", _console.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(_directory);
        }
    }

    // A dry run starts its agent over for every restart, so no restart it asked for is ever still due.
    [Fact]
    public void ADryRunOnlySaysWhereTheRestartWouldBeRecorded()
    {
        WindowsPERestartMarker marker = new(_directory, Log(), dryRun: true);

        marker.Set(RestartInto.WindowsPE);

        Assert.Null(marker.Due);
        Assert.False(Directory.Exists(_directory));
        Assert.Contains(@"Dry run: in Windows PE the due restart would be recorded in X:\DDT\restart-due", _console.ToString(), StringComparison.Ordinal);
    }
}
