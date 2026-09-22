// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

public sealed class RunReportTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static readonly AgentLogBatch s_oneLine = new([new AgentLogLine(DateTimeOffset.UtcNow, AgentLogLevel.Information, "x")]);

    // A machine with the Minimal sequence assigned on the web, as its agent finds it at its next poll.
    private async Task<(DeployingMachine Machine, AgentRun Run)> AssignedAsync()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Minimal(image.Id));

        await administrator.AssignedAsync(machine.Id, sequence.Id);

        return (machine, (await machine.NextAsync()).Run!);
    }

    private static SequenceStep Partition(AgentRun run) => run.Sequence.Steps[0];

    private static SequenceStep Apply(AgentRun run) => run.Sequence.Steps[1];

    private Task<Deployment> StoredAsync(Guid runId) =>
        application.QueryAsync(database => database.Deployments.AsNoTracking().SingleAsync(d => d.Id == runId, TestContext.Current.CancellationToken));

    private async Task<DeploymentView> ViewAsync(Guid runId) => await (await application.AdministratorAsync()).RunAsync(runId);

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
    public async Task TheAgentIsGivenItsRunWithTheFilesItNeeds()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;

        Assert.Equal(DeploymentState.Assigned, run.State);
        Assert.Equal(2, run.Sequence.Steps.Count);
        Assert.Equal(((ApplyImageStep)Apply(run)).ImageId, Assert.Single(run.Images).ImageId);
        Assert.Empty(run.Packages);
        Assert.Null(run.DiskNumber);

        // A lost answer loses nothing: the next poll carries the same run.
        AgentRun again = (await machine.NextAsync()).Run!;

        Assert.Equal(run.Id, again.Id);
        Assert.Equal(run.Images, again.Images);
    }

    [Fact]
    public async Task ARunGoesThroughItsStepsToDone()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;

        await machine.ReportOkAsync(run.Id, Report(DeploymentState.Running, [], activity: RunActivity.Preparing));

        Deployment started = await StoredAsync(run.Id);
        Assert.Equal(DeploymentState.Running, started.State);
        Assert.NotNull(started.StartedUtc);
        Assert.Equal(RunActivity.Preparing, started.Activity);
        Assert.Equal("Admin", RunInputs.Read(started.Inputs!).AdministratorName);
        Assert.Equal(MachineState.Deploying, (await application.MachineAsync(machine.Id)).State);

        await machine.ReportOkAsync(run.Id, Report(DeploymentState.Running, [Step(Partition(run), StepState.Running)], percent: 10));

        DeploymentSummary partitioning = (await ViewAsync(run.Id)).Summary;
        Assert.Equal(0, partitioning.StepIndex);
        Assert.Equal("Partition", partitioning.StepName);
        Assert.Equal(10, partitioning.Percent);
        Assert.Equal(SequencePhase.WindowsPE, partitioning.Phase);
        Assert.Equal(RunActivity.Step, partitioning.Activity);

        AgentRunReport applying = Report(DeploymentState.Running, [Step(Partition(run), StepState.Done), Step(Apply(run), StepState.Running)], percent: 50);
        await machine.ReportOkAsync(run.Id, applying);

        // The response was lost and the agent sends the same again: nothing changes.
        DeploymentView before = await ViewAsync(run.Id);
        await machine.ReportOkAsync(run.Id, applying);
        DeploymentView view = await ViewAsync(run.Id);

        Assert.Equal(before.Steps, view.Steps);
        Assert.Equal(StepState.Done, view.Steps[0].State);
        Assert.Equal(100, view.Steps[0].Percent);
        Assert.NotNull(view.Steps[0].StartedUtc);
        Assert.NotNull(view.Steps[0].FinishedUtc);
        Assert.Equal(StepState.Running, view.Steps[1].State);
        Assert.Equal(50, view.Steps[1].Percent);
        Assert.Null(view.Steps[1].FinishedUtc);
        Assert.Equal(1, view.Summary.StepIndex);

        AgentRunReport done = Report(DeploymentState.Done, [Step(Partition(run), StepState.Done), Step(Apply(run), StepState.Done)], percent: 100);
        await machine.ReportOkAsync(run.Id, done);

        view = await ViewAsync(run.Id);
        Assert.Equal(DeploymentState.Done, view.Summary.State);
        Assert.NotNull(view.Summary.FinishedUtc);
        Assert.All(view.Steps, step => Assert.Equal(StepState.Done, step.State));
        Assert.Equal(MachineState.Done, (await application.MachineAsync(machine.Id)).State);
        Assert.Equal(
            [AuditActions.DeploymentAssigned, AuditActions.DeploymentStarted, AuditActions.DeploymentDone],
            (await AuditAsync(run.Id)).Select(a => a.Split(' ')[0]));
    }

    // A report sent again while the first is still in flight, or one that overlaps a heartbeat, can save after a newer
    // one. It must not move a finished step back, which would also serve that step's secrets again.
    [Fact]
    public async Task AnOlderReportThatSavesLastCannotMoveAFinishedStepBack()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;
        await machine.ReportOkAsync(run.Id, Running());

        using IServiceScope older = application.Services.CreateScope();
        using IServiceScope newer = application.Services.CreateScope();
        DdtDbContext olderDatabase = older.ServiceProvider.GetRequiredService<DdtDbContext>();
        DdtDbContext newerDatabase = newer.ServiceProvider.GetRequiredService<DdtDbContext>();

        DeploymentDecision olderDecision = await older.ServiceProvider.GetRequiredService<RunReports>().ApplyAsync(
            await olderDatabase.Machines.SingleAsync(m => m.Id == machine.Id, cancellationToken),
            run.Id,
            Running(Step(Partition(run), StepState.Running)),
            null,
            cancellationToken);
        DeploymentDecision newerDecision = await newer.ServiceProvider.GetRequiredService<RunReports>().ApplyAsync(
            await newerDatabase.Machines.SingleAsync(m => m.Id == machine.Id, cancellationToken),
            run.Id,
            Running(Step(Partition(run), StepState.Done)),
            null,
            cancellationToken);

        Assert.Equal(DeploymentOutcome.Accepted, olderDecision.Outcome);
        Assert.Equal(DeploymentOutcome.Accepted, newerDecision.Outcome);

        await newerDatabase.SaveChangesAsync(cancellationToken);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => olderDatabase.SaveChangesAsync(cancellationToken));

        Assert.Equal(StepState.Done, (await ViewAsync(run.Id)).Steps[0].State);

        // The endpoint decides the older report again from what is stored now, and refuses it.
        Assert.Equal(HttpStatusCode.Conflict, (await machine.ReportAsync(run.Id, Running(Step(Partition(run), StepState.Running)))).StatusCode);
    }

    // A failed step ends the run unless it may fail, so a run past such a step is not done, whatever the agent says.
    [Fact]
    public async Task ARunIsNotDoneOverAStepThatMustNotFail()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;
        AgentRunReport done = Report(DeploymentState.Done, [Step(Partition(run), StepState.Failed, "diskpart failed."), Step(Apply(run), StepState.Done)]);
        await machine.ReportOkAsync(run.Id, Running(Step(Partition(run), StepState.Failed, "diskpart failed.")));

        HttpResponseMessage refused = await machine.ReportAsync(run.Id, done);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(
            "Step 1, Partition, failed and must not fail, so the run is not done. Report the run as failed.",
            await TestDatabase.TitleAsync(refused));
        Assert.Equal(DeploymentState.Running, (await StoredAsync(run.Id)).State);
        Assert.Equal(MachineState.Deploying, (await application.MachineAsync(machine.Id)).State);
    }

    [Fact]
    public async Task AStepThatMayFailLeavesTheRunDone()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceStep mayFail = SequenceRequests.ScriptOnly().Steps[0] with { ContinueOnError = true };
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Definition(mayFail));
        await administrator.AssignedAsync(machine.Id, sequence.Id);
        AgentRun run = (await machine.NextAsync()).Run!;

        await machine.ReportOkAsync(run.Id, Running(Step(run.Sequence.Steps[0], StepState.Failed, "Exit code 1.")));
        await machine.ReportOkAsync(run.Id, Report(DeploymentState.Done, [Step(run.Sequence.Steps[0], StepState.Failed, "Exit code 1.")]));

        Assert.Equal(DeploymentState.Done, (await StoredAsync(run.Id)).State);
    }

    // Steps a report skips over, such as one the heartbeat missed or one skipped by its conditions, are taken as they
    // are reported.
    [Fact]
    public async Task AStepCanGoStraightFromPendingToItsEnd()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;

        await machine.ReportOkAsync(run.Id, Running(Step(Partition(run), StepState.Done), Step(Apply(run), StepState.Skipped, "The model is Virtual Machine.")));

        DeploymentView view = await ViewAsync(run.Id);

        Assert.Equal(StepState.Done, view.Steps[0].State);
        Assert.NotNull(view.Steps[0].StartedUtc);
        Assert.Equal(StepState.Skipped, view.Steps[1].State);
        Assert.Null(view.Steps[1].StartedUtc);
        Assert.NotNull(view.Steps[1].FinishedUtc);
        Assert.Equal("The model is Virtual Machine.", view.Steps[1].Error);
    }

    [Fact]
    public async Task ACheckBeforeTheStartFailsTheRun()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;

        await machine.ReportOkAsync(run.Id, Report(DeploymentState.Failed, [], error: "The disk is too small for the image."));

        Deployment failed = await StoredAsync(run.Id);
        Assert.Equal(DeploymentState.Failed, failed.State);
        Assert.Equal("The disk is too small for the image.", failed.Error);
        Assert.Null(failed.StartedUtc);
        Assert.Equal(MachineState.Failed, (await application.MachineAsync(machine.Id)).State);
        Assert.Contains($"{AuditActions.DeploymentFailed} {failed.Title} on machine {machine.Id:D}: The disk is too small for the image.", await AuditAsync(run.Id));

        // A failed machine still holds its session token, to be given another run.
        Assert.Equal(HttpStatusCode.NoContent, (await machine.Agent.LogAsync(machine.Id, machine.Token, s_oneLine)).StatusCode);
    }

    [Fact]
    public async Task AFailureEndsTheStepThatRan()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;

        await machine.ReportOkAsync(run.Id, Running(Step(Partition(run), StepState.Done), Step(Apply(run), StepState.Running)));

        // Whatever the report says about steps that do not fit, the failure ends the run.
        await machine.ReportOkAsync(
            run.Id,
            Report(DeploymentState.Failed, [Step(Partition(run), StepState.Pending), new StepRunState(Guid.NewGuid(), StepState.Done, null)], error: "wimlib error 59"));

        DeploymentView view = await ViewAsync(run.Id);

        Assert.Equal(DeploymentState.Failed, view.Summary.State);
        Assert.Equal("wimlib error 59", view.Summary.Error);
        Assert.Equal(StepState.Done, view.Steps[0].State);
        Assert.Equal(StepState.Failed, view.Steps[1].State);
        Assert.Equal("wimlib error 59", view.Steps[1].Error);
        Assert.Contains($"{AuditActions.DeploymentFailed} {view.Summary.Title} on machine {machine.Id:D} at Apply: wimlib error 59", await AuditAsync(run.Id));

        // Sent again, because the answer was lost: answered like the first.
        await machine.ReportOkAsync(run.Id, Report(DeploymentState.Failed, [], error: "wimlib error 59"));
    }

    [Fact]
    public async Task RefusesChangesThatDoNotExist()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;

        HttpResponseMessage early = await machine.ReportAsync(run.Id, Report(DeploymentState.Done, []));
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal("A run that is assigned cannot be reported as done. Ask the server for the current run.", await TestDatabase.TitleAsync(early));

        await machine.ReportOkAsync(run.Id, Running(Step(Partition(run), StepState.Done)));

        HttpResponseMessage back = await machine.ReportAsync(run.Id, Running(Step(Partition(run), StepState.Running)));
        Assert.Equal(HttpStatusCode.Conflict, back.StatusCode);
        Assert.Equal("Step 1, Partition, is done and cannot become running. Ask the server for the current run.", await TestDatabase.TitleAsync(back));

        Assert.Equal(HttpStatusCode.Conflict, (await machine.ReportAsync(run.Id, Running(Step(Partition(run), StepState.Failed)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await machine.ReportAsync(run.Id, Running(new StepRunState(Guid.NewGuid(), StepState.Running, null)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await machine.ReportAsync(run.Id, Running(Step(Apply(run), StepState.Running), Step(Apply(run), StepState.Running)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await machine.ReportAsync(run.Id, Report(DeploymentState.Assigned, []))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await machine.ReportAsync(run.Id, Report(DeploymentState.Running, []) with { CurrentStepId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await machine.ReportAsync(run.Id, Report(DeploymentState.Running, []) with { Phase = (SequencePhase)7 })).StatusCode);

        HttpResponseMessage open = await machine.ReportAsync(run.Id, Report(DeploymentState.Done, [Step(Partition(run), StepState.Done)]));
        Assert.Equal(HttpStatusCode.Conflict, open.StatusCode);
        Assert.StartsWith("Step 2, Apply, is pending, so the run is not done.", await TestDatabase.TitleAsync(open), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NotFound, (await machine.ReportAsync(Guid.NewGuid(), Running())).StatusCode);
        Assert.Equal(DeploymentState.Running, (await StoredAsync(run.Id)).State);
        Assert.Equal(StepState.Done, (await ViewAsync(run.Id)).Steps[0].State);
    }

    // PostgreSQL text cannot hold a NUL and refuses a value longer than its column, and the agent would resend forever.
    [Fact]
    public async Task BoundsWhatTheAgentReports()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;
        string error = "A NUL\0 here " + new string('x', 5000);

        await machine.ReportOkAsync(run.Id, Report(DeploymentState.Running, [Step(Partition(run), StepState.Failed, error)], percent: 250));

        DeploymentView view = await ViewAsync(run.Id);

        Assert.Equal(100, view.Summary.Percent);
        Assert.Equal(DeploymentLimits.MaxErrorLength, view.Steps[0].Error!.Length);
        Assert.StartsWith("A NUL here x", view.Steps[0].Error, StringComparison.Ordinal);

        await machine.ReportOkAsync(run.Id, Report(DeploymentState.Failed, [], error: " \0 "));

        Assert.Equal("The agent reported a failure without saying why.", (await StoredAsync(run.Id)).Error);
    }

    [Fact]
    public async Task AReportCountsAsSeeingTheMachine()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;
        DateTimeOffset longAgo = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);

        await application.ChangeMachineAsync(machine.Id, m => m.LastSeenUtc = longAgo);
        await machine.ReportOkAsync(run.Id, Running());

        Assert.True((await application.MachineAsync(machine.Id)).LastSeenUtc > longAgo);
    }

    [Fact]
    public async Task ARunEndedOnTheWebTakesNoFurtherReports()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        (DeployingMachine cancelled, AgentRun assigned) = await AssignedAsync();
        (DeployingMachine stopped, AgentRun running) = await AssignedAsync();
        using DeployingMachine first = cancelled;
        using DeployingMachine second = stopped;

        (await administrator.EndCurrentAsync(cancelled.Id)).EnsureSuccessStatusCode();
        HttpResponseMessage late = await cancelled.ReportAsync(assigned.Id, Running());

        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal("The run is cancelled and takes no further reports. Ask the server for the current run.", await TestDatabase.TitleAsync(late));

        await stopped.ReportOkAsync(running.Id, Running(Step(Partition(running), StepState.Running)));
        (await administrator.EndCurrentAsync(stopped.Id)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await stopped.ReportAsync(running.Id, Running(Step(Partition(running), StepState.Done)))).StatusCode);

        // The step that ran when the run was stopped ended with it.
        DeploymentStepView partition = (await ViewAsync(running.Id)).Steps[0];
        Assert.Equal(StepState.Failed, partition.State);
        Assert.StartsWith("Stopped by administrator-", partition.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMachineReportsOnlyItsOwnRun()
    {
        (DeployingMachine owner, AgentRun run) = await AssignedAsync();
        using DeployingMachine first = owner;
        using DeployingMachine other = await DeployingMachine.ApprovedAsync(application, await application.AdministratorAsync());

        Assert.Equal(HttpStatusCode.Forbidden, (await other.Agent.RunReportAsync(owner.Id, other.Token, run.Id, Running())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.ReportAsync(run.Id, Running())).StatusCode);
        Assert.Equal(DeploymentState.Assigned, (await StoredAsync(run.Id)).State);
    }

    // An agent throws on a step kind it does not know, so one too old for the sequence never gets the run.
    [Fact]
    public async Task AnAgentTooOldForTheSequenceIsNotGivenTheRun()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;

        AgentRegistrationResult old = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
            await machine.Agent.RegisterAsync(machine.Registration with { ResumeToken = machine.ResumeToken, SequenceVersion = 0 }));
        AgentNextResult next = await RegisteredMachine.ReadAsync<AgentNextResult>(await machine.Agent.NextAsync(machine.Id, old.Token!));

        Assert.Equal(MachineState.Approved, next.State);
        Assert.Null(next.Run);
        Assert.Null(next.Deployment);
        Assert.Equal(run.Id, (await application.MachineAsync(machine.Id)).ActiveDeploymentId);
    }

    // The service in Windows continues a run that the agent in Windows PE started, and never starts one.
    [Fact]
    public async Task TheServiceInWindowsIsGivenOnlyARunThatRuns()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;

        AgentRegistrationResult service = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(
            machine.Registration with { ResumeToken = machine.ResumeToken, Environment = AgentEnvironment.Windows }));

        Assert.Null((await RegisteredMachine.ReadAsync<AgentNextResult>(await machine.Agent.NextAsync(machine.Id, service.Token!))).Run);

        await machine.ReportOkAsync(run.Id, Running(Step(Partition(run), StepState.Running)));

        Assert.Equal(run.Id, (await RegisteredMachine.ReadAsync<AgentNextResult>(await machine.Agent.NextAsync(machine.Id, service.Token!))).Run?.Id);
    }

    // The settings the run needs went away with a restart of the server between the assignment and the start. The run
    // fails at once instead of when the agent asks for them, halfway through.
    [Fact]
    public async Task ARunWhoseSettingsAreGoneFailsAtTheStart()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;
        SequenceDefinition needsAdministrator = SequenceRequests.Definition(
        [
            .. run.Sequence.Steps,
            new WriteUnattendStep { Id = Guid.NewGuid(), Name = "Answer file", LocalAdministrator = true },
        ]);

        await application.QueryAsync(database => database.DeploymentSnapshots
            .Where(s => s.DeploymentId == run.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Definition, SequenceRequests.Json(needsAdministrator)), TestContext.Current.CancellationToken));

        HttpResponseMessage refused = await machine.ReportAsync(run.Id, Running());

        string? reason = await TestDatabase.TitleAsync(refused);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.StartsWith("The sequence adds the local administrator, but DDT:Deployment:LocalAdministrator has no password", reason, StringComparison.Ordinal);

        Deployment failed = await StoredAsync(run.Id);
        Assert.Equal(DeploymentState.Failed, failed.State);
        Assert.Equal(reason, failed.Error);
        Assert.Equal(MachineState.Failed, (await application.MachineAsync(machine.Id)).State);
    }
}
