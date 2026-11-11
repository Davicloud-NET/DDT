// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Applications;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class AgentStepRunnerTests
{
    private static readonly RebootStep s_reboot = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a6"), Name = "Restart" };

    private static readonly InstallApplicationsStep s_install = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000ab"),
        Name = "Install applications",
        Applications = [new ApplicationReference(TestApplications.Tool.Id, ApplicationReferenceKind.Application)],
    };

    [Fact]
    public async Task ARestartStepAsksForTheRestart()
    {
        using WindowsStepRunnerFixture run = new([s_reboot]);

        StepResult result = await run.Steps.RunAsync(s_reboot, run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepResult.RebootRequired(), result);
    }

    [Theory]
    [InlineData(SequencePhase.WindowsPE, "Windows PE")]
    [InlineData(SequencePhase.LinuxBoot, "the Linux boot environment")]
    public async Task JoiningTheDomainFailsInABootEnvironmentWhichTheFailureNames(SequencePhase phase, string name)
    {
        JoinDomainStep step = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a7"), Name = "Join" };
        using WindowsStepRunnerFixture run = new([step]);

        StepResult result = await run.Steps.RunAsync(step, run.Context(phase), TestContext.Current.CancellationToken);

        Assert.Equal(
            StepResult.Failed($"Joining the domain runs in Windows, after the hand-over, but this agent was asked to run it in {name}."),
            result);
    }

    [Theory]
    [InlineData(SequencePhase.WindowsPE, "Applications install in Windows, after the hand-over, but this agent was asked to install them in Windows PE.")]
    [InlineData(SequencePhase.LinuxBoot, "Applications install in Windows, after the hand-over, but this agent was asked to install them in the Linux boot environment.")]
    [InlineData(SequencePhase.Linux, "Applications install in Windows, but this agent was asked to install them in the installed Linux.")]
    public async Task InstallingApplicationsFailsOutsideTheInstalledWindowsWithoutAskingTheServer(SequencePhase phase, string error)
    {
        using WindowsStepRunnerFixture run = new([s_install]);

        StepResult result = await run.Steps.RunAsync(s_install, run.Context(phase), TestContext.Current.CancellationToken);

        Assert.Equal(StepResult.Failed(error), result);
        Assert.Empty(run.Server.Calls);
    }

    [Fact]
    public async Task AStepThatRestartsAndGoesOnSaysSo()
    {
        using WindowsStepRunnerFixture run = new([s_install]);
        run.Session.RunRoot = Path.Combine(run.Tools.Root, "C");
        run.Server.OnRunApplications(_ => new AgentStepApplications([TestApplications.Runtime, TestApplications.Tool]));
        run.ToolRunner.AnswerExitCode = (_, _, _) => 3010;

        StepResult result = await run.Steps.RunAsync(s_install, run.Context(SequencePhase.Windows), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.RestartAndContinue, result.Outcome);
        Assert.Equal(
            "Step Install applications restarts the machine after 0 s, and goes on after the restart.",
            (await run.SentLinesAsync())[^1].Message);
    }

    [Fact]
    public async Task EveryLineOfAStepNamesTheStep()
    {
        using WindowsStepRunnerFixture run = new([s_reboot]);
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
        using WindowsStepRunnerFixture run = new([step]);
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
        using WindowsStepRunnerFixture run = new([step]);
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
        using WindowsStepRunnerFixture run = new([step]);
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
