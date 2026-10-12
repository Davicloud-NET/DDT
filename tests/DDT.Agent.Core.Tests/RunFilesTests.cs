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

    // DDT on a Windows volume, and the root itself elsewhere.
    private string RunDirectory => RunFiles.DirectoryIn(_windows);
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

        Assert.Equal(Path.Combine(RunDirectory, "run", "state.json"), _files.StatePath);
        Assert.Equal(Path.Combine(RunDirectory, "run", "token"), _files.TokenPath);
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
        Assert.Equal(["state.json", "token"], Directory.EnumerateFiles(Path.Combine(RunDirectory, "run")).Select(Path.GetFileName).Order());
    }

    // The vendor-data script installs them for root alone, and a save puts a new file in the place of each.
    [Fact]
    public async Task EachFileStaysItsOwnersAloneThroughEverySave()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has no file modes.");
        AgentRunReport report = new(DeploymentState.Failed, SequencePhase.Linux, [], null, 0, RunActivity.Step, "The step failed.");

        for (int save = 0; save < 2; save++)
        {
            await _files.SaveStateAsync(s_state, TestContext.Current.CancellationToken);
            await _files.SaveTokenAsync($"run-token-{save}", TestContext.Current.CancellationToken);
            await _files.SaveFinalReportAsync(report, TestContext.Current.CancellationToken);
        }

        foreach (string file in (string[])[_files.StatePath, _files.TokenPath, _files.FinalReportPath])
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
            }
        }
    }

    // A save that lost power left its temporary file, and one that had already replaced the file left the old one.
    [Fact]
    public async Task ASaveTakesAwayWhatAnEarlierSaveLeftBehind()
    {
        await _files.SaveTokenAsync("run-token-1", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(_files.TokenPath + FileReplacement.Temporary, "half a tok", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(_files.StatePath + FileReplacement.Replaced, "{}", TestContext.Current.CancellationToken);

        _files.Recover();

        Assert.Equal(["token"], Directory.EnumerateFiles(Path.Combine(RunDirectory, "run")).Select(Path.GetFileName));
        Assert.Equal("run-token-1", await _files.LoadTokenAsync(TestContext.Current.CancellationToken));
        Assert.Empty(_console.ToString());
    }

    // An overwrite that reached the disk before its rename did: zeros under the name, and the save's own copy beside it.
    [Fact]
    public async Task AtAStartAWholeCopyTakesThePlaceOfAFileThatCannotBeRead()
    {
        await _files.SaveTokenAsync("run-token-1", TestContext.Current.CancellationToken);
        await _files.SaveStateAsync(s_state, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(_files.TokenPath, new byte[11], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(_files.StatePath, new byte[400], TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(_files.TokenPath + FileReplacement.Temporary, "run-token-2", TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(_files.StatePath + FileReplacement.Temporary, RunFiles.StateContent(s_state with { NextIndex = 2 }), TestContext.Current.CancellationToken);

        _files.Recover();

        Assert.Equal("run-token-2", await _files.LoadTokenAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, (await _files.LoadStateAsync(TestContext.Current.CancellationToken))?.NextIndex);
        Assert.Equal(["state.json", "token"], Directory.EnumerateFiles(Path.Combine(RunDirectory, "run")).Select(Path.GetFileName).Order());
        Assert.Contains(
            $"WARN  {_files.TokenPath} could not be read after a save that was cut off. The copy that save had written whole is in its place now.",
            _console.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACopyThatIsNotWholeTakesNoPlace()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_files.StatePath)!);
        await File.WriteAllBytesAsync(_files.TokenPath, new byte[11], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(_files.StatePath, new byte[400], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(_files.TokenPath + FileReplacement.Temporary, [(byte)'r', 0, 0], TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(_files.StatePath + FileReplacement.Temporary, "{ \"format\": 2, \"run", TestContext.Current.CancellationToken);

        _files.Recover();

        Assert.Equal(new byte[11], await File.ReadAllBytesAsync(_files.TokenPath, TestContext.Current.CancellationToken));
        Assert.Equal(["state.json", "token"], Directory.EnumerateFiles(Path.Combine(RunDirectory, "run")).Select(Path.GetFileName).Order());
        Assert.Empty(_console.ToString());
    }

    // An overwritten token must not be sent to the server as a line of NUL characters.
    [Fact]
    public async Task ATokenFileOfZerosHoldsNoToken()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_files.TokenPath)!);
        await File.WriteAllBytesAsync(_files.TokenPath, new byte[64], TestContext.Current.CancellationToken);

        Assert.Null(await _files.LoadTokenAsync(TestContext.Current.CancellationToken));
    }

    public static TheoryData<string> LinkedNames => ["token" + FileReplacement.Temporary, "token" + FileReplacement.Replaced, "token"];

    // File.Exists follows a link, so one that points nowhere looks like nothing, and a plain create would write its target.
    [Theory]
    [MemberData(nameof(LinkedNames))]
    public async Task ALinkThatPointsNowhereIsNeitherWrittenThroughNorLeft(string name)
    {
        string run = Path.Combine(RunDirectory, "run");
        string target = Path.Combine(_windows, "planted");
        Directory.CreateDirectory(run);
        LinkOrSkip(Path.Combine(run, name), target);

        await _files.SaveTokenAsync("run-token-1", TestContext.Current.CancellationToken);
        await _files.SaveTokenAsync("run-token-2", TestContext.Current.CancellationToken);

        Assert.False(File.Exists(target));
        Assert.Equal("run-token-2", await _files.LoadTokenAsync(TestContext.Current.CancellationToken));
        Assert.Equal(["token"], Directory.EnumerateFileSystemEntries(run).Select(Path.GetFileName));
        Assert.Null(new FileInfo(_files.TokenPath).LinkTarget);
    }

    [Fact]
    public async Task DiscardRemovesALinkThatPointsNowhereItself()
    {
        await _files.SaveTokenAsync("run-token-1", TestContext.Current.CancellationToken);
        string target = Path.Combine(_windows, "planted");
        LinkOrSkip(_files.StatePath, target);

        _files.Discard();

        Assert.False(Directory.Exists(Path.Combine(RunDirectory, "run")));
        Assert.False(File.Exists(target));
        Assert.Empty(_console.ToString());
    }

    // Windows lets only an administrator or a developer's computer make a link.
    private static void LinkOrSkip(string path, string target)
    {
        try
        {
            File.CreateSymbolicLink(path, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"This system lets the test make no link ({exception.Message}).");
        }
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

    // A tree's run continues after a restart from one entry per node in pre-order, plus its cursor, which may sit
    // inside a repeat.
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

        Assert.Equal(Path.Combine(RunDirectory, "run", "final-report.json"), _files.FinalReportPath);
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

        Assert.False(Directory.Exists(Path.Combine(RunDirectory, "run")));
        Assert.True(Directory.Exists(RunDirectory));
    }

    // Partition, then a repeat around a script, stopped at the script on its second pass.
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
