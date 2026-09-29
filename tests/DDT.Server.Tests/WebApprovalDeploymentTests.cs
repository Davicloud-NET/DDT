// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Authentication;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

// Under RequireWebApproval a machine needs both halves. Someone signs in at it, and an operator acts on the web.
// Either order works, and a web assignment counts as acting on the web.
public sealed class WebApprovalDeploymentTests(WebApprovalApplication application) : IClassFixture<WebApprovalApplication>
{
    private async Task<MachineSummary> AssignAsync(Guid machineId)
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly(), $"Hello {Guid.NewGuid():N}");

        return await RegisteredMachine.ReadAsync<MachineSummary>(await administrator.AssignAsync(machineId, sequence.Id));
    }

    private static async Task<AgentSignInResult> SignInAsync(DeployingMachine machine, string userName) =>
        await RegisteredMachine.ReadAsync<AgentSignInResult>(
            await machine.Agent.SignInAsync(machine.Id, machine.Token, new AgentSignInRequest(userName, DdtApplication.Password, null)));

    [Fact]
    public async Task AnAssignmentWaitsForASignInThatThenApprovesTheMachine()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);

        MachineSummary assigned = await AssignAsync(machine.Id);

        // It's fresh, but nobody vouched for it at the machine.
        Assert.Equal(MachineState.Pending, assigned.State);
        Assert.Null((await machine.NextAsync()).Run);

        Assert.Equal(AgentSignInStatus.Succeeded, (await SignInAsync(machine, operatorName)).Status);

        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(MachineState.Approved, next.State);
        Assert.Equal(assigned.Deployment?.Id, (await application.MachineAsync(machine.Id)).ActiveDeploymentId);

        string subject = machine.Id.ToString("D");
        List<string?> approvals = await application.QueryAsync(database => database.AuditEvents
            .Where(e => e.SubjectId == subject && e.Action == AuditActions.MachineApproved)
            .Select(e => e.Detail)
            .ToListAsync(TestContext.Current.CancellationToken));

        Assert.Contains(" had assigned Hello ", Assert.Single(approvals), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASignInThenAnAssignmentApprovesTheMachine()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);

        Assert.Equal(AgentSignInStatus.Succeeded, (await SignInAsync(machine, operatorName)).Status);
        Assert.Equal(MachineState.Pending, (await machine.NextAsync()).State);

        // Being seen long ago doesn't matter here. The sign-in at the machine happened in this generation.
        await application.ChangeMachineAsync(machine.Id, m => m.LastSeenUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10));

        MachineSummary assigned = await AssignAsync(machine.Id);

        Assert.Equal(MachineState.Approved, assigned.State);

        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(assigned.Deployment?.Id, (await application.MachineAsync(machine.Id)).ActiveDeploymentId);
        Assert.False(next.CanPickSequence);
    }

    [Fact]
    public async Task ASignInWithoutAnAssignmentStillWaitsForTheWeb()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);

        Assert.Equal(AgentSignInStatus.Succeeded, (await SignInAsync(machine, operatorName)).Status);

        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(MachineState.Pending, next.State);
        Assert.False(next.CanPickSequence);
    }
}
