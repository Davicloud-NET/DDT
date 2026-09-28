// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

public sealed class ZeroTouchTests(ZeroTouchApplication application) : IClassFixture<ZeroTouchApplication>
{
    // Assigned on the web while the machine wasn't at its prompt, so the assignment waits for its next netboot.
    internal static async Task<Guid> AssignWhileAwayAsync(DdtApplication application, DeployingMachine machine)
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly(), $"Hello {Guid.NewGuid():N}");
        await application.ChangeMachineAsync(machine.Id, m => m.LastSeenUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10));

        MachineSummary summary = await RegisteredMachine.ReadAsync<MachineSummary>(await administrator.AssignAsync(machine.Id, sequence.Id));

        Assert.Equal(MachineState.Pending, summary.State);

        return summary.Deployment!.Id;
    }

    private Task<Deployment> StoredAsync(Guid deploymentId) =>
        application.QueryAsync(database => database.Deployments.AsNoTracking().SingleAsync(d => d.Id == deploymentId, TestContext.Current.CancellationToken));

    private Task<AuditEvent> AuditAsync(Guid deploymentId, string action)
    {
        string subject = deploymentId.ToString("D");

        return application.QueryAsync(database => database.AuditEvents
            .AsNoTracking()
            .SingleAsync(e => e.SubjectId == subject && e.Action == action, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ANetbootFromAListedNetworkKeepsTheAssignmentAuthorized()
    {
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);
        Guid deployment = await AssignWhileAwayAsync(application, machine);
        using AgentClient lab = new(application.CreateDefaultClient(), "10.200.3.4");
        string before = machine.Token;

        AgentRegistrationResult registered = await machine.RegisterAgainAsync(lab);

        Assert.Equal(MachineState.Approved, registered.State);

        // A poll token from before the netboot is invalid, whoever registered for it.
        // That's true even though Approved accepts poll tokens.
        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.Agent.NextAsync(machine.Id, before)).StatusCode);

        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(MachineState.Approved, next.State);
        Assert.Null(next.SignedInBy);

        Machine stored = await application.MachineAsync(machine.Id);
        string subject = machine.Id.ToString("D");
        string detail = (await application.QueryAsync(database => database.AuditEvents
            .Where(e => e.SubjectId == subject && e.Action == AuditActions.MachineReregistered)
            .Select(e => e.Detail)
            .SingleAsync(TestContext.Current.CancellationToken)))!;

        Assert.Equal(deployment, stored.ActiveDeploymentId);
        Assert.Equal(DeploymentState.Assigned, (await StoredAsync(deployment)).State);
        Assert.NotNull(stored.FirstApprovedUtc);
        Assert.NotNull(stored.ApprovedByUserId);
        Assert.StartsWith("Kept approved for Hello ", detail, StringComparison.Ordinal);
        Assert.Contains(" assigned by administrator-", detail, StringComparison.Ordinal);
        Assert.EndsWith(": netbooted from 10.200.3.4 in a zero touch network.", detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("::ffff:10.200.9.9")]
    [InlineData("fd00:200::42")]
    public async Task MatchesIPv4MappedAndIPv6Addresses(string address)
    {
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);
        Guid deployment = await AssignWhileAwayAsync(application, machine);
        using AgentClient lab = new(application.CreateDefaultClient(), address);

        Assert.Equal(MachineState.Approved, (await machine.RegisterAgainAsync(lab)).State);
        Assert.Equal(deployment, (await application.MachineAsync(machine.Id)).ActiveDeploymentId);
    }

    [Fact]
    public async Task ANetbootFromElsewhereWaitsForASignIn()
    {
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);
        Guid deployment = await AssignWhileAwayAsync(application, machine);
        using AgentClient elsewhere = new(application.CreateDefaultClient(), TestRemoteAddress.Unique());

        AgentRegistrationResult registered = await machine.RegisterAgainAsync(elsewhere);

        Assert.Equal(MachineState.Pending, registered.State);
        Assert.Equal(DeploymentState.Assigned, (await StoredAsync(deployment)).State);
        Assert.Null((await machine.NextAsync()).Run);

        // The assignment survives, and a sign-in at the machine starts it.
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        AgentSignInResult signedIn = await RegisteredMachine.ReadAsync<AgentSignInResult>(
            await machine.Agent.SignInAsync(machine.Id, machine.Token, new AgentSignInRequest(operatorName, DdtApplication.Password, null)));

        Assert.Equal(AgentSignInStatus.Succeeded, signedIn.Status);

        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(MachineState.Approved, next.State);
        Assert.Equal(deployment, (await application.MachineAsync(machine.Id)).ActiveDeploymentId);
        Assert.False(next.CanPickSequence);
    }

    [Fact]
    public async Task ARunningRunFailsWhenTheMachineStartsAgain()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());
        Guid deployment = (await administrator.AssignedAsync(machine.Id, sequence.Id)).Id;

        await machine.NextAsync();
        await application.MoveRunAsync(machine.Id, DeploymentState.Running);

        // Resuming with the resume token means it's the same agent after an outage, so nothing changes.
        AgentRegistrationResult resumed = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
            await machine.Agent.RegisterAsync(machine.Registration with { ResumeToken = machine.ResumeToken }));

        Assert.Equal(MachineState.Deploying, resumed.State);
        Assert.Equal(DeploymentState.Running, (await StoredAsync(deployment)).State);

        using AgentClient lab = new(application.CreateDefaultClient(), "10.200.7.7");
        AgentRegistrationResult restarted = await machine.RegisterAgainAsync(lab);

        Deployment failed = await StoredAsync(deployment);

        Assert.Equal(MachineState.Pending, restarted.State);
        Assert.Equal(DeploymentState.Failed, failed.State);
        Assert.Equal("The machine started again during the run, without the run's token, so the run could not continue.", failed.Error);
        Assert.Null((await application.MachineAsync(machine.Id)).ActiveDeploymentId);

        AuditEvent audit = await AuditAsync(deployment, AuditActions.DeploymentFailed);

        Assert.Equal(machine.Id, audit.ActorMachineId);
        Assert.Null(audit.ActorUserId);
        Assert.Equal("10.200.7.7", audit.SourceAddress);
        Assert.Equal($"{failed.Title} on machine {machine.Id:D}. {failed.Error}", audit.Detail);
    }

    // The disk and the ERASE were typed at the machine in the boot that ended, and its disk numbers can differ
    // after the restart. Whoever signs in next chooses again.
    [Fact]
    public async Task AChoiceMadeAtTheMachineEndsWhenItStartsAgain()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(
            application,
            operatorName,
            [DeployingMachine.Disk(0), DeployingMachine.Disk(1, "Samsung SSD 990 PRO")]);
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));
        SequenceView sequence = await (await application.AdministratorAsync()).CreatedSequenceAsync(SequenceRequests.Minimal(image.Id));

        AgentRun picked = await RegisteredMachine.ReadAsync<AgentRun>(
            await machine.Agent.PickRunAsync(machine.Id, machine.Token, new AgentRunRequest(sequence.Id, 1, null)));

        using AgentClient lab = new(application.CreateDefaultClient(), "10.200.8.8");

        // Not zero touch either, although it netboots from a listed network.
        Assert.Equal(MachineState.Pending, (await machine.RegisterAgainAsync(lab)).State);

        Deployment cancelled = await StoredAsync(picked.Id);
        AuditEvent audit = await AuditAsync(picked.Id, AuditActions.DeploymentCancelled);

        Assert.Equal(DeploymentState.Cancelled, cancelled.State);
        Assert.Null(cancelled.Error);
        Assert.NotNull(cancelled.FinishedUtc);
        Assert.Null((await application.MachineAsync(machine.Id)).ActiveDeploymentId);
        Assert.Equal(machine.Id, audit.ActorMachineId);
        Assert.Equal(
            $"{sequence.Name} on machine {machine.Id:D}. The machine started again before the run began.",
            audit.Detail);

        AgentSignInResult signedIn = await RegisteredMachine.ReadAsync<AgentSignInResult>(
            await machine.Agent.SignInAsync(machine.Id, machine.Token, new AgentSignInRequest(operatorName, DdtApplication.Password, null)));

        Assert.Equal(AgentSignInStatus.Succeeded, signedIn.Status);

        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(MachineState.Approved, next.State);
        Assert.Null(next.Run);
        Assert.True(next.CanPickSequence);
    }

    [Fact]
    public async Task TheAssignDialogLearnsThatZeroTouchIsOn()
    {
        DeploymentOptionsView options = await RegisteredMachine.ReadAsync<DeploymentOptionsView>(
            await (await application.AdministratorAsync()).GetAsync("/api/deployments/options"));

        Assert.False(options.RequireWebApproval);
        Assert.True(options.ZeroTouchEnabled);
    }

    [Fact]
    public async Task WebApprovalTurnsZeroTouchOff()
    {
        using SettingsApplication strict = new(
            ("DDT:Machines:ZeroTouchNetworks", "10.200.0.0/16"),
            ("DDT:Machines:RequireWebApproval", "true"));
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(strict);
        await AssignWhileAwayAsync(strict, machine);
        using AgentClient lab = new(strict.CreateDefaultClient(), "10.200.3.5");

        Assert.Equal(MachineState.Pending, (await machine.RegisterAgainAsync(lab)).State);

        DeploymentOptionsView options = await RegisteredMachine.ReadAsync<DeploymentOptionsView>(
            await (await strict.AdministratorAsync()).GetAsync("/api/deployments/options"));

        Assert.True(options.RequireWebApproval);
        Assert.False(options.ZeroTouchEnabled);
    }
}
