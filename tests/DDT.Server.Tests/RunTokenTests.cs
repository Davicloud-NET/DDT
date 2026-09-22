// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

// A run survives the restarts it asks for: the agent keeps the run token on disk and continues with it.
public sealed class RunTokenTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    // A run that has started, with the run token the agent keeps.
    internal static async Task<(DeployingMachine Machine, AgentRun Run)> RunningAsync(DdtApplication application)
    {
        SignedInClient administrator = await application.AdministratorAsync();
        DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());

        await administrator.AssignedAsync(machine.Id, sequence.Id);
        AgentRun run = (await machine.NextAsync()).Run!;
        await machine.ReportOkAsync(run.Id, Running(Step(run.Sequence.Steps[0], StepState.Running)));

        return (machine, run);
    }

    // As the agent registers after the restart: a new process, without the resume token, with the run token.
    private static async Task<HttpResponseMessage> ContinueAsync(DeployingMachine machine, string? runToken, AgentEnvironment environment = AgentEnvironment.WindowsPE) =>
        await machine.Agent.RegisterAsync(machine.Registration with { RunToken = runToken, Environment = environment });

    private Task<List<string>> AuditAsync(Guid subjectId)
    {
        string subject = subjectId.ToString("D");

        return application.QueryAsync(database => database.AuditEvents
            .Where(e => e.SubjectId == subject)
            .OrderBy(e => e.Id)
            .Select(e => e.Action + " " + e.SourceAddress + " " + e.Detail)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARestartedAgentContinuesItsRunWithTheRunToken()
    {
        (DeployingMachine machine, AgentRun run) = await RunningAsync(application);
        using DeployingMachine _ = machine;
        string runToken = Assert.IsType<string>(machine.RunToken);
        string before = machine.Token;
        int generation = (await application.MachineAsync(machine.Id)).TokenGeneration;

        AgentRegistrationResult continued = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await ContinueAsync(machine, runToken));

        Assert.Equal(MachineState.Deploying, continued.State);
        Assert.Equal(run.Id, continued.RunId);
        Assert.NotNull(continued.RunToken);
        Assert.Equal(generation, (await application.MachineAsync(machine.Id)).TokenGeneration);

        // The generation stays, so what the agent held before the restart still works.
        Assert.Equal(HttpStatusCode.OK, (await machine.Agent.NextAsync(machine.Id, before)).StatusCode);

        AgentNextResult next = await RegisteredMachine.ReadAsync<AgentNextResult>(await machine.Agent.NextAsync(machine.Id, continued.Token!));
        Assert.Equal(run.Id, next.Run?.Id);
        Assert.Equal(DeploymentState.Running, next.Run?.State);

        // Not rotated: a lost answer to the registration leaves the agent with a token that still works.
        Assert.Equal(run.Id, (await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await ContinueAsync(machine, runToken))).RunId);

        // Every one of them is audited with the address it came from.
        Assert.Equal(
            2,
            (await AuditAsync(run.Id)).Count(a => a == $"{AuditActions.DeploymentResumed} {machine.Agent.RemoteAddress} Continued {run.SequenceName} ({run.Id:D}) from WindowsPE with its run token."));
    }

    // An agent that kept its resume token continues the run with it just as well. It is told so, or the service in
    // Windows would take the run for over and remove itself while the run stays running.
    [Fact]
    public async Task TheResumeTokenContinuesARunningRunAsTheRunTokenDoes()
    {
        (DeployingMachine machine, AgentRun run) = await RunningAsync(application);
        using DeployingMachine _ = machine;
        int generation = (await application.MachineAsync(machine.Id)).TokenGeneration;

        AgentRegistrationResult service = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(
            machine.Registration with { ResumeToken = machine.ResumeToken, Environment = AgentEnvironment.Windows }));

        Assert.Equal(MachineState.Deploying, service.State);
        Assert.Equal(run.Id, service.RunId);
        Assert.NotNull(service.RunToken);
        Assert.Equal(generation, (await application.MachineAsync(machine.Id)).TokenGeneration);
        Assert.Contains(
            $"{AuditActions.DeploymentResumed} {machine.Agent.RemoteAddress} Continued {run.SequenceName} ({run.Id:D}) from Windows with its resume token.",
            await AuditAsync(run.Id));
    }

    [Fact]
    public async Task EveryAnswerWhileTheRunRunsCarriesARunToken()
    {
        (DeployingMachine machine, AgentRun run) = await RunningAsync(application);
        using DeployingMachine _ = machine;

        HttpResponseMessage response = await machine.Agent.RunReportAsync(machine.Id, machine.Token, run.Id, Running(Step(run.Sequence.Steps[0], StepState.Done)));
        AgentRunReportResult result = await RegisteredMachine.ReadAsync<AgentRunReportResult>(response);

        Assert.NotNull(result.RunToken);

        await machine.ReportOkAsync(run.Id, Report(DeploymentState.Failed, [], error: "Stopped."));
        AgentRunReportResult failed = await RegisteredMachine.ReadAsync<AgentRunReportResult>(
            await machine.Agent.RunReportAsync(machine.Id, machine.Token, run.Id, Report(DeploymentState.Failed, [], error: "Stopped.")));

        Assert.Null(failed.RunToken);
    }

    // The token lets its holder act as the machine for this run, so it works only at registration.
    [Fact]
    public async Task ARunTokenIsNoBearerToken()
    {
        (DeployingMachine machine, AgentRun run) = await RunningAsync(application);
        using DeployingMachine _ = machine;

        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.Agent.NextAsync(machine.Id, machine.RunToken!)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.Agent.RunReportAsync(machine.Id, machine.RunToken!, run.Id, Running())).StatusCode);
    }

    [Fact]
    public async Task ARunTokenDiesWithItsRun()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        (DeployingMachine machine, AgentRun run) = await RunningAsync(application);
        using DeployingMachine _ = machine;
        string runToken = machine.RunToken!;

        (await administrator.EndCurrentAsync(machine.Id)).EnsureSuccessStatusCode();

        AgentRegistrationResult after = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await ContinueAsync(machine, runToken));

        Assert.Equal(MachineState.Pending, after.State);
        Assert.Null(after.RunId);
        Assert.Null(after.RunToken);
        Assert.StartsWith("Stopped by ", (await administrator.RunAsync(run.Id)).Summary.Error, StringComparison.Ordinal);
        Assert.Contains(await AuditAsync(machine.Id), a => a.StartsWith(AuditActions.DeploymentRunTokenRefused, StringComparison.Ordinal));
    }

    // Another machine that presents the token is not the machine the token was issued to.
    [Fact]
    public async Task ARunTokenBelongsToItsMachine()
    {
        (DeployingMachine machine, AgentRun run) = await RunningAsync(application);
        using DeployingMachine _ = machine;
        using DeployingMachine other = await DeployingMachine.ApprovedAsync(application, await application.AdministratorAsync());

        AgentRegistrationResult stranger = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await ContinueAsync(other, machine.RunToken));

        Assert.Equal(MachineState.Pending, stranger.State);
        Assert.Null(stranger.RunId);
        Assert.Equal(DeploymentState.Running, (await (await application.AdministratorAsync()).RunAsync(run.Id)).Summary.State);
    }

    [Fact]
    public async Task ARestartWithoutTheRunTokenFailsTheRun()
    {
        (DeployingMachine machine, AgentRun run) = await RunningAsync(application);
        using DeployingMachine _ = machine;

        Assert.Equal(MachineState.Pending, (await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await ContinueAsync(machine, null))).State);

        DeploymentView failed = await (await application.AdministratorAsync()).RunAsync(run.Id);

        Assert.Equal(DeploymentState.Failed, failed.Summary.State);
        Assert.Equal("The machine started again during the run, without the run's token, so the run could not continue.", failed.Summary.Error);
        Assert.Equal(StepState.Failed, failed.Steps[0].State);
        Assert.Equal(failed.Summary.Error, failed.Steps[0].Error);
    }

    // Without the guard, a stopped service would register the installed Windows as a waiting machine, and an approval
    // would hand it the Windows PE steps of a new run.
    [Fact]
    public async Task TheServiceInWindowsWithNothingToContinueChangesNothing()
    {
        (DeployingMachine machine, AgentRun run) = await RunningAsync(application);
        using DeployingMachine _ = machine;
        Machine before = await application.MachineAsync(machine.Id);
        int audits = (await AuditAsync(machine.Id)).Count;

        HttpResponseMessage refused = await ContinueAsync(machine, null, AgentEnvironment.Windows);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("This machine has no run for the DDT service to continue. The service removes itself.", await TestDatabase.TitleAsync(refused));

        Machine after = await application.MachineAsync(machine.Id);
        Assert.Equal((before.State, before.TokenGeneration, before.ActiveDeploymentId, before.LastSeenUtc), (after.State, after.TokenGeneration, after.ActiveDeploymentId, after.LastSeenUtc));
        Assert.Equal(audits, (await AuditAsync(machine.Id)).Count);
        Assert.Equal(DeploymentState.Running, (await (await application.AdministratorAsync()).RunAsync(run.Id)).Summary.State);

        // Nor does one the server has never seen become a machine.
        using AgentClient stranger = new(application.CreateDefaultClient(), TestRemoteAddress.Unique());
        AgentRegistration unknown = AgentClient.Registration(Guid.NewGuid().ToString("D"), "02AABBCCDDEE") with { Environment = AgentEnvironment.Windows };
        Assert.Equal(HttpStatusCode.Conflict, (await stranger.RegisterAsync(unknown)).StatusCode);
        Assert.False(await application.QueryAsync(database => database.Machines.AnyAsync(m => m.SmbiosUuid == unknown.SmbiosUuid, TestContext.Current.CancellationToken)));

        // With the run token, the service continues the run.
        AgentRegistrationResult service = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
            await ContinueAsync(machine, machine.RunToken, AgentEnvironment.Windows));

        Assert.Equal(run.Id, service.RunId);
        Assert.Equal(AgentEnvironment.Windows, (await application.MachineAsync(machine.Id)).AgentEnvironment);
    }
}
