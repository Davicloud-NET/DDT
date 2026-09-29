// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

// A password given for one run lives only while the run is assigned or running, whichever way the run ends.
public sealed class RunCredentialCleanupTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static RunCredential Credential(Guid runId, string inputName) => new()
    {
        DeploymentId = runId,
        InputName = inputName,
        UserName = @"CORP\alice",
        ProtectedPassword = "ciphertext",
        Domain = "corp.example.com",
        ProvidedAtMachine = true,
        CreatedUtc = DateTimeOffset.UtcNow,
    };

    // Assigns a sequence without problems on the web and gives two accounts for it.
    private async Task<(DeployingMachine Machine, AgentRun Run)> AssignedAsync()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceView sequence = await application.RunnableSequenceAsync();

        await administrator.AssignedAsync(machine.Id, sequence.Id);
        AgentRun run = (await machine.NextAsync()).Run!;

        await application.QueryAsync(database =>
        {
            database.RunCredentials.AddRange(Credential(run.Id, "JoinAccount"), Credential(run.Id, "ShareAccount"));

            return database.SaveChangesAsync(Cancellation);
        });

        return (machine, run);
    }

    private async Task<(DeployingMachine Machine, AgentRun Run)> RunningAsync()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();

        await machine.ReportOkAsync(run.Id, Running(Step(run.Sequence.Steps[0], StepState.Running)));

        return (machine, run);
    }

    private Task<int> CredentialsAsync(Guid runId) =>
        application.QueryAsync(database => database.RunCredentials.CountAsync(c => c.DeploymentId == runId, Cancellation));

    private Task<DeploymentState> StateAsync(Guid runId) =>
        application.QueryAsync(database => database.Deployments.Where(d => d.Id == runId).Select(d => d.State).SingleAsync(Cancellation));

    [Fact]
    public async Task ARunningRunKeepsItsCredentialsAcrossItsReports()
    {
        (DeployingMachine machine, AgentRun run) = await RunningAsync();
        using DeployingMachine _ = machine;

        await machine.ReportOkAsync(run.Id, Running(Step(run.Sequence.Steps[0], StepState.Done), Step(run.Sequence.Steps[1], StepState.Running)));

        Assert.Equal(DeploymentState.Running, await StateAsync(run.Id));
        Assert.Equal(2, await CredentialsAsync(run.Id));
    }

    [Fact]
    public async Task ARunThatIsDoneKeepsNone()
    {
        (DeployingMachine machine, AgentRun run) = await RunningAsync();
        using DeployingMachine _ = machine;

        await machine.ReportOkAsync(
            run.Id,
            Report(DeploymentState.Done, [Step(run.Sequence.Steps[0], StepState.Done), Step(run.Sequence.Steps[1], StepState.Done)]) with { Percent = 100 });

        Assert.Equal(DeploymentState.Done, await StateAsync(run.Id));
        Assert.Equal(0, await CredentialsAsync(run.Id));
    }

    [Fact]
    public async Task ARunThatFailsKeepsNone()
    {
        (DeployingMachine machine, AgentRun run) = await RunningAsync();
        using DeployingMachine _ = machine;

        await machine.ReportOkAsync(run.Id, Report(DeploymentState.Failed, [Step(run.Sequence.Steps[0], StepState.Failed, "No disk.")]) with { Error = "No disk." });

        Assert.Equal(DeploymentState.Failed, await StateAsync(run.Id));
        Assert.Equal(0, await CredentialsAsync(run.Id));
    }

    [Fact]
    public async Task ARunThatIsCancelledOrStoppedKeepsNone()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        (DeployingMachine assigned, AgentRun cancelled) = await AssignedAsync();
        (DeployingMachine running, AgentRun stopped) = await RunningAsync();
        using DeployingMachine _ = assigned;
        using DeployingMachine __ = running;

        (await administrator.EndCurrentAsync(assigned.Id)).EnsureSuccessStatusCode();
        (await administrator.EndCurrentAsync(running.Id)).EnsureSuccessStatusCode();

        Assert.Equal((DeploymentState.Cancelled, 0), (await StateAsync(cancelled.Id), await CredentialsAsync(cancelled.Id)));
        Assert.Equal((DeploymentState.Failed, 0), (await StateAsync(stopped.Id), await CredentialsAsync(stopped.Id)));
    }

    [Fact]
    public async Task TheRunsOfARejectedMachineKeepNone()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        (DeployingMachine assigned, AgentRun cancelled) = await AssignedAsync();
        (DeployingMachine running, AgentRun failed) = await RunningAsync();
        using DeployingMachine _ = assigned;
        using DeployingMachine __ = running;

        (await administrator.PostAsync($"/api/machines/{assigned.Id}/reject")).EnsureSuccessStatusCode();
        (await administrator.PostAsync($"/api/machines/{running.Id}/reject")).EnsureSuccessStatusCode();

        Assert.Equal((DeploymentState.Cancelled, 0), (await StateAsync(cancelled.Id), await CredentialsAsync(cancelled.Id)));
        Assert.Equal((DeploymentState.Failed, 0), (await StateAsync(failed.Id), await CredentialsAsync(failed.Id)));
    }

    // Another agent took the machine over, so the run it had fails.
    // A web assignment that hasn't started stays for the next boot and keeps what was given for it.
    [Fact]
    public async Task ARunThatEndsBecauseTheMachineStartedAgainKeepsNone()
    {
        (DeployingMachine waiting, AgentRun kept) = await AssignedAsync();
        (DeployingMachine running, AgentRun failed) = await RunningAsync();
        using DeployingMachine _ = waiting;
        using DeployingMachine __ = running;

        await waiting.RegisterAgainAsync();
        await running.RegisterAgainAsync();

        Assert.Equal((DeploymentState.Assigned, 2), (await StateAsync(kept.Id), await CredentialsAsync(kept.Id)));
        Assert.Equal((DeploymentState.Failed, 0), (await StateAsync(failed.Id), await CredentialsAsync(failed.Id)));
    }

    [Fact]
    public async Task ARunWhoseAgentWasLostKeepsNone()
    {
        (DeployingMachine machine, AgentRun run) = await RunningAsync();
        using DeployingMachine _ = machine;

        await using (AsyncServiceScope scope = application.Services.CreateAsyncScope())
        {
            DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
            RunTermination termination = scope.ServiceProvider.GetRequiredService<RunTermination>();
            Machine loaded = await database.Machines.SingleAsync(m => m.Id == machine.Id, Cancellation);
            Deployment active = await database.Deployments.SingleAsync(d => d.Id == loaded.ActiveDeploymentId, Cancellation);

            await termination.EndForLostContactAsync(loaded, active, Cancellation);
            await database.SaveChangesAsync(Cancellation);
        }

        Assert.Equal((DeploymentState.Failed, 0), (await StateAsync(run.Id), await CredentialsAsync(run.Id)));
    }

    // The credentials are removed in the same save that ends the run.
    // So a save that fails keeps them with the run it didn't end.
    [Fact]
    public async Task ASaveThatFailsToEndTheRunKeepsThem()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;

        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        RunTermination termination = scope.ServiceProvider.GetRequiredService<RunTermination>();
        Machine loaded = await database.Machines.SingleAsync(m => m.Id == machine.Id, Cancellation);

        // A registration changes the machine in between.
        await application.ChangeMachineAsync(machine.Id, m => m.TokenGeneration++);
        await termination.EndCurrentAsync(loaded, new Actor(null, "alice", null), Cancellation);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => database.SaveChangesAsync(Cancellation));
        Assert.Equal((DeploymentState.Assigned, 2), (await StateAsync(run.Id), await CredentialsAsync(run.Id)));
    }

    [Fact]
    public async Task ACredentialGivenInTheSaveThatEndsTheRunIsNeverStored()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;

        await using (AsyncServiceScope scope = application.Services.CreateAsyncScope())
        {
            DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
            RunTermination termination = scope.ServiceProvider.GetRequiredService<RunTermination>();
            Machine loaded = await database.Machines.SingleAsync(m => m.Id == machine.Id, Cancellation);

            database.RunCredentials.Add(Credential(run.Id, "LateAccount"));
            await termination.EndCurrentAsync(loaded, new Actor(null, "alice", null), Cancellation);
            await database.SaveChangesAsync(Cancellation);
        }

        Assert.Equal((DeploymentState.Cancelled, 0), (await StateAsync(run.Id), await CredentialsAsync(run.Id)));
    }

    // A run that ended without a save through this build, for example by another build, loses its credentials at the
    // next start. A run that's still going keeps them.
    [Fact]
    public async Task TheSweepAtTheStartRemovesTheCredentialsOfRunsThatAreOver()
    {
        (DeployingMachine ended, AgentRun over) = await AssignedAsync();
        (DeployingMachine running, AgentRun goesOn) = await RunningAsync();
        using DeployingMachine _ = ended;
        using DeployingMachine __ = running;

        await application.QueryAsync(database => database.Deployments
            .Where(d => d.Id == over.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(d => d.State, DeploymentState.Failed), Cancellation));
        Assert.Equal(2, await CredentialsAsync(over.Id));

        int removed = await application.Services.GetRequiredService<RunCredentialSweeper>().SweepOnceAsync(Cancellation);

        Assert.Equal(2, removed);
        Assert.Equal(0, await CredentialsAsync(over.Id));
        Assert.Equal(2, await CredentialsAsync(goesOn.Id));
    }
}
