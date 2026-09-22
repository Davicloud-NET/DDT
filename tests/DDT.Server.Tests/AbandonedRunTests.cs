// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using System.Threading.Channels;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

// The server's clock stands still, so a machine last seen a given time ago is exactly that old when the sweep runs.
public sealed class AbandonedRunTests(ManualClockApplication application) : IClassFixture<ManualClockApplication>
{
    // Last seen lags a report by up to its resolution, and the run token that report handed out lasts from the report.
    private static readonly TimeSpan s_mayStillResume = MachineTokenLifetimes.Run + MachineLogLimits.LastSeenResolution;

    private static readonly TimeSpan s_canNoLongerResume = s_mayStillResume + TimeSpan.FromSeconds(1);

    private static string Error(DateTimeOffset lastSeen) =>
        $"The agent has not been in contact since {lastSeen:u} and could no longer resume the run.";

    // A run of the Minimal sequence at its first step, with the image it downloads locked in the library.
    private async Task<(DeployingMachine Machine, AgentRun Run)> RunningAsync()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Minimal(image.Id));

        await administrator.AssignedAsync(machine.Id, sequence.Id);
        AgentRun run = (await machine.NextAsync()).Run!;
        await machine.ReportOkAsync(run.Id, Running(Step(run.Sequence.Steps[0], StepState.Running)));

        return (machine, run);
    }

    private async Task<DateTimeOffset> LastSeenAsync(Guid machineId, TimeSpan ago)
    {
        DateTimeOffset lastSeen = application.Clock.GetUtcNow() - ago;

        await application.ChangeMachineAsync(machineId, m => m.LastSeenUtc = lastSeen);

        return lastSeen;
    }

    private Task<int> SweepAsync() =>
        application.Services.GetRequiredService<AbandonedRunSweeper>().SweepOnceAsync(TestContext.Current.CancellationToken);

    private Task<bool> LockedAsync(Guid runId) =>
        application.QueryAsync(database => ActiveArtifacts.Of(database).AnyAsync(a => a.DeploymentId == runId, TestContext.Current.CancellationToken));

    private Task<List<string>> AuditAsync(Guid runId)
    {
        string subject = runId.ToString("D");

        return application.QueryAsync(database => database.AuditEvents
            .Where(e => e.SubjectId == subject)
            .OrderBy(e => e.Id)
            .Select(e => e.Action + " " + e.Detail)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARunWhoseAgentWasSilentLongerThanItsRunTokenLastsFails()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        (DeployingMachine machine, AgentRun run) = await RunningAsync();
        using DeployingMachine _ = machine;
        int generation = (await application.MachineAsync(machine.Id)).TokenGeneration;
        DateTimeOffset lastSeen = await LastSeenAsync(machine.Id, s_canNoLongerResume);

        Assert.True(await LockedAsync(run.Id));
        Assert.Equal(1, await SweepAsync());

        string error = Error(lastSeen);
        DeploymentView failed = await administrator.RunAsync(run.Id);
        Machine after = await application.MachineAsync(machine.Id);

        Assert.Equal(DeploymentState.Failed, failed.Summary.State);
        Assert.Equal(error, failed.Summary.Error);
        Assert.Equal(StepState.Failed, failed.Steps[0].State);
        Assert.Equal(error, failed.Steps[0].Error);
        Assert.Equal(MachineState.Failed, after.State);
        Assert.Null(after.ActiveDeploymentId);
        Assert.Equal(generation + 1, after.TokenGeneration);
        Assert.False(await LockedAsync(run.Id));
        Assert.Contains($"{AuditActions.DeploymentFailed} {run.SequenceName} on machine {machine.Id:D}. {error}", await AuditAsync(run.Id));
        Assert.Equal(0, await SweepAsync());

        // Its tokens died with the run.
        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.Agent.NextAsync(machine.Id, machine.Token)).StatusCode);

        AgentRegistrationResult again = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
            await machine.Agent.RegisterAsync(machine.Registration with { RunToken = machine.RunToken }));

        Assert.Null(again.RunId);
        Assert.Equal(MachineState.Pending, again.State);
    }

    // Open Machines pages show the run fail, and a page that shows the machine shows its step fail.
    [Fact]
    public async Task TheFailureIsPushedAsForAnyOtherEnd()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener listener = await LiveListener.StartAsync(application, administrator);
        ChannelReader<MachineSummary> pushes = listener.Listen<MachineSummary>(LiveEvents.MachineChanged);
        ChannelReader<RunStepChangedEvent> steps = listener.Listen<RunStepChangedEvent>(LiveEvents.RunStepChanged);
        (DeployingMachine machine, AgentRun run) = await RunningAsync();
        using DeployingMachine _ = machine;

        await listener.WatchAsync(machine.Id);
        string error = Error(await LastSeenAsync(machine.Id, s_canNoLongerResume));
        Assert.Equal(1, await SweepAsync());

        // The row's push waits for the second after its last push to end.
        application.Clock.Advance(LiveNotifier.MachinePushInterval);

        MachineSummary pushed = await LiveListener.NextAsync(pushes, m => m.Id == machine.Id && m.State == MachineState.Failed);
        RunStepChangedEvent step = await LiveListener.NextAsync(steps, e => e.Step.State == StepState.Failed);

        Assert.Equal(run.Id, pushed.Deployment?.Id);
        Assert.Equal(DeploymentState.Failed, pushed.Deployment?.State);
        Assert.Equal(error, pushed.Deployment?.Error);
        Assert.Equal(machine.Id, step.MachineId);
        Assert.Equal(run.Id, step.DeploymentId);
        Assert.Equal(run.Sequence.Steps[0].Id, step.Step.StepId);
        Assert.Equal(error, step.Step.Error);
    }

    // Nothing here calls the sweep: the server runs it every hour.
    [Fact]
    public async Task TheServerSweepsEveryHour()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        (DeployingMachine machine, AgentRun run) = await RunningAsync();
        using DeployingMachine _ = machine;

        await LastSeenAsync(machine.Id, s_canNoLongerResume);

        DeploymentState state = DeploymentState.Running;

        // Every pass moves the clock, so the sweep also comes when the sweeper's timer started after the first move.
        for (int attempt = 0; attempt < 200 && state == DeploymentState.Running; attempt++)
        {
            application.Clock.Advance(TimeSpan.FromHours(1));
            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
            state = (await administrator.RunAsync(run.Id)).Summary.State;
        }

        Assert.Equal(DeploymentState.Failed, state);
    }

    // Waiting for an interactive Windows setup can take days, and the agent reports all along.
    [Fact]
    public async Task ARunThatStillReportsIsNotFailed()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        (DeployingMachine machine, AgentRun run) = await RunningAsync();
        using DeployingMachine _ = machine;

        await LastSeenAsync(machine.Id, s_mayStillResume);
        await SweepAsync();

        Assert.Equal(DeploymentState.Running, (await administrator.RunAsync(run.Id)).Summary.State);

        await LastSeenAsync(machine.Id, s_canNoLongerResume);
        await machine.ReportOkAsync(run.Id, Report(
            DeploymentState.Running,
            [Step(run.Sequence.Steps[0], StepState.Done), Step(run.Sequence.Steps[1], StepState.Done)],
            phase: SequencePhase.Windows,
            activity: RunActivity.WaitingForWindowsSetup));
        await SweepAsync();

        Assert.Equal(DeploymentState.Running, (await administrator.RunAsync(run.Id)).Summary.State);
        Assert.Equal(MachineState.Deploying, (await application.MachineAsync(machine.Id)).State);
        Assert.True(await LockedAsync(run.Id));
    }

    // A web assignment waits on purpose, for the machine's next netboot or a sign-in at it.
    [Fact]
    public async Task AnAssignedRunWaitsHoweverLongItsMachineIsAway()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());
        DeploymentSummary assigned = await administrator.AssignedAsync(machine.Id, sequence.Id);

        await LastSeenAsync(machine.Id, s_canNoLongerResume);
        await SweepAsync();

        Assert.Equal(DeploymentState.Assigned, (await administrator.RunAsync(assigned.Id)).Summary.State);
        Assert.Equal(assigned.Id, (await application.MachineAsync(machine.Id)).ActiveDeploymentId);
    }
}
