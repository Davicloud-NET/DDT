// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class RunFilesTests : IDisposable
{
    private static readonly SequenceState s_state = SequenceStates.Start(
        Guid.Parse("0193a4b2-0000-7000-8000-0000000000f1"),
        new SequenceDefinition(
            SequenceDefinition.CurrentVersion,
            [
                new PartitionStep { Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a1"), Name = "Partition" },
                new RebootStep { Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a2"), Name = "Restart" },
            ])) with
    {
        NextIndex = 1,
        Variables = new Dictionary<string, string> { [RunVariables.WindowsPartition] = "0193a4b2-0000-7000-8000-0000000000e1" },
    };

    private static readonly RunScriptStep s_insideRepeat = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a3"),
        Name = "Until it works",
        Phase = SequencePhase.WindowsPE,
        Script = "exit /b 0",
    };

    private readonly string _windows = Directory.CreateTempSubdirectory("ddt-run-files-").FullName;
    private readonly StringWriter _console = new();
    private readonly RunFiles _files;

    public RunFilesTests()
    {
        _files = RunFiles.In(_windows, new AgentLog(new ImmediateTimeProvider(), _console));
    }

    public void Dispose()
    {
        _console.Dispose();
        Directory.Delete(_windows, recursive: true);
    }

    [Fact]
    public async Task KeepsTheStateAndTheTokenInTheRunsDirectory()
    {
        await _files.SaveStateAsync(s_state, TestContext.Current.CancellationToken);
        await _files.SaveTokenAsync("run-token-1", TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(_windows, "DDT", "run", "state.json"), _files.StatePath);
        Assert.Equal(Path.Combine(_windows, "DDT", "run", "token"), _files.TokenPath);
        Assert.Equal(RunFiles.StatePathIn(_windows), _files.StatePath);
        Assert.Equal(Json(s_state), Json(await _files.LoadStateAsync(TestContext.Current.CancellationToken)));
        Assert.Equal("run-token-1", await _files.LoadTokenAsync(TestContext.Current.CancellationToken));
        Assert.Equal("run-token-1", await File.ReadAllTextAsync(_files.TokenPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplacesEachFileWhole()
    {
        await _files.SaveStateAsync(s_state, TestContext.Current.CancellationToken);
        await _files.SaveTokenAsync("run-token-1", TestContext.Current.CancellationToken);
        await _files.SaveStateAsync(s_state with { NextIndex = 2 }, TestContext.Current.CancellationToken);
        await _files.SaveTokenAsync("run-token-2", TestContext.Current.CancellationToken);

        Assert.Equal(2, (await _files.LoadStateAsync(TestContext.Current.CancellationToken))?.NextIndex);
        Assert.Equal("run-token-2", await _files.LoadTokenAsync(TestContext.Current.CancellationToken));
        Assert.Equal(["state.json", "token"], Directory.EnumerateFiles(Path.Combine(_windows, "DDT", "run")).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task NoFilesMeanNoRun()
    {
        Assert.Null(await _files.LoadStateAsync(TestContext.Current.CancellationToken));
        Assert.Null(await _files.LoadTokenAsync(TestContext.Current.CancellationToken));
        Assert.Empty(_console.ToString());
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{"format":1,"runId":"0193a4b2-0000-7000-8000-0000000000f1","definition":{"version":1,"steps":[{"kind":"writeRawImage","id":"0193a4b2-0000-7000-8000-0000000000a1","name":"Raw"}]},"phase":"WindowsPE","nextIndex":0,"steps":[],"variables":{}}""")]
    [InlineData("""{"format":1,"runId":"0193a4b2-0000-7000-8000-0000000000f1","definition":{"version":1,"steps":[]},"phase":"WindowsPE","nextIndex":0,"steps":[{"stepId":"0193a4b2-0000-7000-8000-0000000000a1","state":"Done","error":null}],"variables":{}}""")]
    public async Task AStateThatCannotBeReadCountsAsNoRun(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_files.StatePath)!);
        await File.WriteAllTextAsync(_files.StatePath, json, TestContext.Current.CancellationToken);

        Assert.Null(await _files.LoadStateAsync(TestContext.Current.CancellationToken));
        Assert.Contains($"WARN  {_files.StatePath} ", _console.ToString(), StringComparison.Ordinal);
    }

    // A tree's run goes on after a restart from an entry per node in pre-order and its cursor, which may sit inside a
    // repeat.
    [Fact]
    public async Task KeepsATreesStateWithItsCursor()
    {
        SequenceState tree = TreeState();

        await _files.SaveStateAsync(tree, TestContext.Current.CancellationToken);
        SequenceState? loaded = await _files.LoadStateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Json(tree), Json(loaded));
        Assert.Equal((SequenceState.TreeFormat, SequenceStates.NoNextIndex), (loaded?.Format, loaded?.NextIndex));
        Assert.Equal(new NodeCursor(s_insideRepeat.Id, false), loaded?.Cursor);
        Assert.Empty(_console.ToString());
    }

    public static TheoryData<string> BrokenTrees =>
    [
        "an entry short",
        "entries out of order",
        "an index",
        "a cursor elsewhere",
    ];

    [Theory]
    [MemberData(nameof(BrokenTrees))]
    public async Task ATreesStateThatDoesNotFitItsTreeCountsAsNoRun(string broken)
    {
        SequenceState tree = TreeState();
        tree = broken switch
        {
            "an entry short" => tree with { Steps = [.. tree.Steps.Take(2)] },
            "entries out of order" => tree with { Steps = [.. tree.Steps.Reverse()] },
            "an index" => tree with { NextIndex = 1 },
            _ => tree with { Cursor = new NodeCursor(Guid.Parse("0193a4b2-0000-7000-8000-0000000000ff"), false) },
        };

        await _files.SaveStateAsync(tree, TestContext.Current.CancellationToken);

        Assert.Null(await _files.LoadStateAsync(TestContext.Current.CancellationToken));
        Assert.Contains($"WARN  {_files.StatePath} does not describe a run this agent can go on with", _console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeepsHowTheRunEndedInTheRunsDirectory()
    {
        AgentRunReport done = new(
            DeploymentState.Done,
            SequencePhase.Windows,
            [new StepRunState(Guid.Parse("0193a4b2-0000-7000-8000-0000000000a1"), StepState.Done, null)],
            null,
            100,
            RunActivity.Finishing,
            null);

        await _files.SaveFinalReportAsync(done, TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(_windows, "DDT", "run", "final-report.json"), _files.FinalReportPath);
        Assert.Equal(Json(done), Json(await _files.LoadFinalReportAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task AFinalReportThatCannotBeReadIsNoneAfterAWarning()
    {
        Assert.Null(await _files.LoadFinalReportAsync(TestContext.Current.CancellationToken));
        Assert.Empty(_console.ToString());

        Directory.CreateDirectory(Path.GetDirectoryName(_files.FinalReportPath)!);
        await File.WriteAllTextAsync(_files.FinalReportPath, "{ not json", TestContext.Current.CancellationToken);

        Assert.Null(await _files.LoadFinalReportAsync(TestContext.Current.CancellationToken));
        Assert.Contains($"WARN  {_files.FinalReportPath} cannot be read", _console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscardRemovesTheRunsFiles()
    {
        await _files.SaveStateAsync(s_state, TestContext.Current.CancellationToken);
        await _files.SaveTokenAsync("run-token-1", TestContext.Current.CancellationToken);
        await _files.SaveFinalReportAsync(new AgentRunReport(DeploymentState.Failed, SequencePhase.Windows, [], null, 0, RunActivity.Step, "The step failed."), TestContext.Current.CancellationToken);

        _files.Discard();

        Assert.False(Directory.Exists(Path.Combine(_windows, "DDT", "run")));
        Assert.True(Directory.Exists(Path.Combine(_windows, "DDT")));
    }

    // Partition, then a repeat around a script, stopped at the script in its second time through.
    private static SequenceState TreeState()
    {
        RepeatStep repeat = new()
        {
            Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a2"),
            Name = "Try again",
            Steps = [s_insideRepeat],
            Until = new TestCondition(MachineVariableNames.LastExitCode, ConditionOperator.Equals, "0"),
        };
        SequenceDefinition definition = new(
            SequenceDefinition.CurrentVersion,
            [new PartitionStep { Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a1"), Name = "Partition" }, repeat]);
        SequenceState start = SequenceStates.Start(Guid.Parse("0193a4b2-0000-7000-8000-0000000000f1"), definition);

        return start with
        {
            Steps =
            [
                new StepRunState(start.Steps[0].StepId, StepState.Done, null, Pass: 1),
                new StepRunState(repeat.Id, StepState.Running, null, Pass: 1, Iteration: 2),
                new StepRunState(s_insideRepeat.Id, StepState.Pending, null, Pass: 1),
            ],
            Cursor = new NodeCursor(s_insideRepeat.Id, false),
            Variables = new Dictionary<string, string> { [MachineVariableNames.LastExitCode] = "1" },
        };
    }

    private static string Json(SequenceState? state) =>
        JsonSerializer.Serialize(state, AgentJsonContext.Default.SequenceState);

    private static string Json(AgentRunReport? report) =>
        JsonSerializer.Serialize(report, AgentJsonContext.Default.AgentRunReport);
}
