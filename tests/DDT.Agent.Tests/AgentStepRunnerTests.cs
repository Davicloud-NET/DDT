// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class AgentStepRunnerTests
{
    private static readonly RebootStep s_reboot = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a6"), Name = "Restart" };

    [Fact]
    public async Task ARestartStepAsksForTheRestart()
    {
        using StepRunnerFixture run = new([s_reboot]);

        StepResult result = await run.Steps.RunAsync(s_reboot, run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepResult.RebootRequired(), result);
    }

    [Fact]
    public async Task JoiningTheDomainFailsInWindowsPE()
    {
        JoinDomainStep step = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a7"), Name = "Join" };
        using StepRunnerFixture run = new([step]);

        StepResult result = await run.Steps.RunAsync(step, run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepResult.Failed(AgentStepRunner.JoinDomainInWindowsPE), result);
    }

    [Fact]
    public async Task EveryLineOfAStepNamesTheStep()
    {
        using StepRunnerFixture run = new([s_reboot]);
        run.Log.Information("Before the step.");

        await run.Steps.RunAsync(s_reboot, run.Context(), TestContext.Current.CancellationToken);
        run.Log.Information("After the step.");

        List<AgentLogLine> lines = await run.SentLinesAsync();
        Assert.Equal(
            [
                (null, "Before the step."),
                (s_reboot.Id, "Step Restart begins."),
                (s_reboot.Id, "Step Restart finished after 0 s. The machine restarts before the next step."),
                (null, "After the step."),
            ],
            lines.Select(line => (line.StepId, line.Message)));
    }

    [Fact]
    public async Task AStepThatThrowsFailsWithItsMessage()
    {
        PartitionStep step = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a8"), Name = "Partition" };
        using StepRunnerFixture run = new([step]);
        run.Session.Disk = null;

        StepResult result = await run.Steps.RunAsync(step, run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepResult.Failed("No disk was chosen for this run, so nothing was partitioned."), result);
        Assert.Contains(
            await run.SentLinesAsync(),
            line => line.Level == AgentLogLevel.Error && line.StepId == step.Id
                && line.Message == "Step Partition failed after 0 s: No disk was chosen for this run, so nothing was partitioned.");
    }

    [Fact]
    public async Task ARefusedTokenEndsTheRunAndReachesTheEngine()
    {
        WriteUnattendStep step = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000aa"), Name = "Answer file" };
        using StepRunnerFixture run = new([step]);
        run.Partitioned();
        AgentTokenRejectedException refused = new();
        run.Server.OnRunUnattend(_ => throw refused);

        await Assert.ThrowsAsync<AgentTokenRejectedException>(() => run.Steps.RunAsync(step, run.Context(), TestContext.Current.CancellationToken));

        Assert.Same(refused, Assert.Single(run.TokenRejections));
    }

    [Fact]
    public async Task AStopReachesTheEngineAsTheStop()
    {
        RunScriptStep step = new()
        {
            Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a9"),
            Name = "Wait",
            Script = "ping -n 30 127.0.0.1",
        };
        using StepRunnerFixture run = new([step]);
        using CancellationTokenSource stop = new();
        run.ToolRunner.AnswerExitCode = (_, _, _) =>
        {
            stop.Cancel();
            stop.Token.ThrowIfCancellationRequested();

            return 0;
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() => run.Steps.RunAsync(step, run.Context(), stop.Token));

        Assert.Null(run.Log.StepId);
    }
}
