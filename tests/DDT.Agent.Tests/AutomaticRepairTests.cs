// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.WindowsPhase;
using Xunit;

namespace DDT.Agent.Tests;

// Automatic Repair off while a run goes on in Windows, and back as it was once the run is over.
public sealed class AutomaticRepairTests : IDisposable
{
    private static readonly string s_turnOff = Bcdedit("/set", "{current}", "bootstatuspolicy", "IgnoreAllFailures");

    private readonly FakeDeploymentTools _tools = new();
    private readonly RecordingToolRunner _toolRunner = new();
    private readonly StringWriter _console = new();
    private int? _policy;
    private int _reads;

    public AutomaticRepairTests() => Directory.CreateDirectory(Path.Combine(Windows, "DDT"));

    public void Dispose()
    {
        _tools.Dispose();
        _console.Dispose();
    }

    private string Windows => _tools.Volumes.Windows;

    private string Kept => AutomaticRepair.FilePathIn(Windows);

    [Fact]
    public async Task AnEntryWithoutAPolicyGetsNoneBackAfterTheRun()
    {
        AutomaticRepair repair = Repair();

        await repair.TurnOffAsync(TestContext.Current.CancellationToken);
        Assert.Equal("none", await File.ReadAllTextAsync(Kept, TestContext.Current.CancellationToken));

        await repair.PutBackAsync(TestContext.Current.CancellationToken);

        Assert.Equal([s_turnOff, Bcdedit("/deletevalue", "{current}", "bootstatuspolicy")], _toolRunner.Calls);
        Assert.False(File.Exists(Kept));
        Assert.DoesNotContain("WARN", _console.ToString(), StringComparison.Ordinal);
    }

    // A restart starts the service again, which must not take its own setting for the one before the run.
    [Fact]
    public async Task OnlyTheFirstStartReadsWhatWasSet()
    {
        _policy = 3;
        await Repair().TurnOffAsync(TestContext.Current.CancellationToken);
        _policy = 1;
        await Repair().TurnOffAsync(TestContext.Current.CancellationToken);

        await Repair().PutBackAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, _reads);
        Assert.Equal([s_turnOff, s_turnOff, Bcdedit("/set", "{current}", "bootstatuspolicy", "IgnoreBootFailures")], _toolRunner.Calls);
    }

    [Theory]
    [InlineData(0, "DisplayAllFailures")]
    [InlineData(1, "IgnoreAllFailures")]
    [InlineData(2, "IgnoreShutdownFailures")]
    [InlineData(3, "IgnoreBootFailures")]
    public async Task APolicyIsPutBackByItsName(int value, string name)
    {
        _policy = value;
        await Repair().TurnOffAsync(TestContext.Current.CancellationToken);

        await Repair().PutBackAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Bcdedit("/set", "{current}", "bootstatuspolicy", name), _toolRunner.Calls[^1]);
    }

    [Fact]
    public async Task AValueBcdeditHasNoNameForIsLeftAlone()
    {
        _policy = 6;

        await Repair().TurnOffAsync(TestContext.Current.CancellationToken);
        await Repair().PutBackAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_toolRunner.Calls);
        Assert.Contains("WARN  Windows' Automatic Repair stays on: bootstatuspolicy is 6", _console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStoreThatCannotBeReadLeavesItOn()
    {
        AutomaticRepair repair = new(Windows, _toolRunner, () => throw new UnauthorizedAccessException("Access is denied."), Log());

        await repair.TurnOffAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_toolRunner.Calls);
        Assert.False(File.Exists(Kept));
        Assert.Contains("WARN  Windows' Automatic Repair stays on: what it is set to could not be read (Access is denied.).", _console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABcdeditThatFailsIsAWarning()
    {
        _toolRunner.Answer = (_, _) => throw new DeploymentStepException("bcdedit.exe failed with exit code 1.");
        AutomaticRepair repair = Repair();

        await repair.TurnOffAsync(TestContext.Current.CancellationToken);
        await repair.PutBackAsync(TestContext.Current.CancellationToken);

        Assert.Contains("WARN  Windows' Automatic Repair could not be turned off (bcdedit.exe failed with exit code 1.).", _console.ToString(), StringComparison.Ordinal);
        Assert.Contains("WARN  Windows' Automatic Repair could not be put back as it was (bcdedit.exe failed with exit code 1.).", _console.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(Kept));
    }

    [Fact]
    public async Task WithoutARunThatTurnedItOffNothingIsPutBack()
    {
        await Repair().PutBackAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_toolRunner.Calls);
    }

    [Theory]
    [InlineData(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }, 1)]
    [InlineData(new byte[] { 3, 0, 0, 0 }, 3)]
    [InlineData(new byte[] { 0 }, 0)]
    public void AnIntegerElementIsReadLeastSignificantFirst(byte[] element, int value)
    {
        Assert.Equal(value, BootStatusPolicy.ValueOf(element));
    }

    [Fact]
    public void AnElementThatIsNoIntegerIsInvalid()
    {
        Assert.Throws<InvalidDataException>(() => BootStatusPolicy.ValueOf("IgnoreAllFailures"));
        Assert.Throws<InvalidDataException>(() => BootStatusPolicy.ValueOf(new byte[9]));
        Assert.Throws<InvalidDataException>(() => BootStatusPolicy.ValueOf(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }));
    }

    private static string Bcdedit(params string[] arguments) => RecordingToolRunner.CommandLine(AutomaticRepair.BcdeditPath, arguments);

    private AutomaticRepair Repair() => new(Windows, _toolRunner, Read, Log());

    private int? Read()
    {
        _reads++;

        return _policy;
    }

    private AgentLog Log() => new(new ImmediateTimeProvider(), _console);
}
