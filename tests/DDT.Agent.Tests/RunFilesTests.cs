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

    private static string Json(SequenceState? state) =>
        JsonSerializer.Serialize(state, AgentJsonContext.Default.SequenceState);

    private static string Json(AgentRunReport? report) =>
        JsonSerializer.Serialize(report, AgentJsonContext.Default.AgentRunReport);
}
