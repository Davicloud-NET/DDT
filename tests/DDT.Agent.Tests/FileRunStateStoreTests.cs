// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class FileRunStateStoreTests : IDisposable
{
    private static readonly SequenceState s_state = SequenceStates.Start(
        Guid.Parse("0193a4b2-0000-7000-8000-0000000000f1"),
        new SequenceDefinition(
            SequenceDefinition.CurrentVersion,
            [new RebootStep { Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a1"), Name = "Restart" }]));

    private readonly string _windows = Directory.CreateTempSubdirectory("ddt-store-").FullName;
    private readonly RunFiles _files;
    private readonly List<SequenceState> _passedOn = [];

    public FileRunStateStoreTests()
    {
        _files = RunFiles.In(_windows, new AgentLog(new ImmediateTimeProvider(), TextWriter.Null));
    }

    public void Dispose() => Directory.Delete(_windows, recursive: true);

    [Fact]
    public async Task KeepsTheStateInMemoryUntilTheRunHasItsDirectory()
    {
        FileRunStateStore store = new(new DeploymentTokens("session", "resume", "run-token-1"), _passedOn.Add);

        await store.SaveAsync(s_state, TestContext.Current.CancellationToken);

        Assert.Same(s_state, store.State);
        Assert.Null(store.Files);
        Assert.False(Directory.Exists(Path.Combine(_windows, "DDT")));
        Assert.Equal([s_state], _passedOn);
    }

    [Fact]
    public async Task WritesWhatItHoldsOnceTheRunHasItsDirectory()
    {
        FileRunStateStore store = new(new DeploymentTokens("session", "resume", "run-token-1"));
        await store.SaveAsync(s_state with { NextIndex = 1 }, TestContext.Current.CancellationToken);

        await store.AttachAsync(_files, TestContext.Current.CancellationToken);

        Assert.Same(_files, store.Files);
        Assert.Equal(1, (await _files.LoadStateAsync(TestContext.Current.CancellationToken))?.NextIndex);
        Assert.Equal("run-token-1", await _files.LoadTokenAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WritesEveryStateAndTheNewestToken()
    {
        DeploymentTokens tokens = new("session", "resume", "run-token-1");
        FileRunStateStore store = new(tokens, _passedOn.Add);
        await store.AttachAsync(_files, TestContext.Current.CancellationToken);

        await store.SaveAsync(s_state, TestContext.Current.CancellationToken);
        tokens.Update("session-2", "resume-2", "run-token-2");
        await store.SaveAsync(s_state with { NextIndex = 1 }, TestContext.Current.CancellationToken);

        Assert.Equal(1, (await _files.LoadStateAsync(TestContext.Current.CancellationToken))?.NextIndex);
        Assert.Equal("run-token-2", await _files.LoadTokenAsync(TestContext.Current.CancellationToken));
        Assert.Equal([0, 1], _passedOn.Select(state => state.NextIndex));
    }

    [Fact]
    public async Task WritesANewTokenBeforeARestartToo()
    {
        DeploymentTokens tokens = new("session", "resume", "run-token-1");
        FileRunStateStore store = new(tokens);
        await store.AttachAsync(_files, TestContext.Current.CancellationToken);

        tokens.Update("session-2", "resume-2", "run-token-2");
        await store.SaveTokenAsync(TestContext.Current.CancellationToken);

        Assert.Equal("run-token-2", await _files.LoadTokenAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnAnswerWithoutARunTokenKeepsTheOneThereIs()
    {
        DeploymentTokens tokens = new("session", "resume", "run-token-1");
        FileRunStateStore store = new(tokens);
        await store.AttachAsync(_files, TestContext.Current.CancellationToken);

        tokens.Update("session-2", "resume-2");
        await store.SaveAsync(s_state, TestContext.Current.CancellationToken);

        Assert.Equal("run-token-1", tokens.RunToken);
        Assert.Equal("run-token-1", await _files.LoadTokenAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WritesNoTokenBeforeTheServerIssuedOne()
    {
        FileRunStateStore store = new(new DeploymentTokens("session", "resume"));

        await store.AttachAsync(_files, TestContext.Current.CancellationToken);
        await store.SaveAsync(s_state, TestContext.Current.CancellationToken);

        Assert.False(File.Exists(_files.TokenPath));
        Assert.True(File.Exists(_files.StatePath));
    }
}
