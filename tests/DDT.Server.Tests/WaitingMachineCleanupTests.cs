// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

public sealed class WaitingMachineCleanupTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private async Task LastSeenAsync(Guid machineId, TimeSpan ago)
    {
        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        Machine machine = await database.Machines.SingleAsync(m => m.Id == machineId, TestContext.Current.CancellationToken);

        machine.LastSeenUtc = DateTimeOffset.UtcNow - ago;
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<bool> ExistsAsync(Guid machineId)
    {
        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();

        return await database.Machines.AnyAsync(m => m.Id == machineId, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SweepsOnlyStaleMachinesNobodyApproved()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using RegisteredMachine stale = await application.RegisterMachineAsync();
        using RegisteredMachine fresh = await application.RegisterMachineAsync();
        using RegisteredMachine approvedOnce = await application.RegisterMachineAsync();

        // Approved, then booted again without its resume token: waiting again, but vouched for once.
        (await administrator.PostAsync($"/api/machines/{approvedOnce.Id}/approve")).EnsureSuccessStatusCode();
        (await approvedOnce.Agent.RegisterAsync(approvedOnce.Registration)).EnsureSuccessStatusCode();

        await LastSeenAsync(stale.Id, TimeSpan.FromDays(2));
        await LastSeenAsync(approvedOnce.Id, TimeSpan.FromDays(2));

        int removed = await application.Services.GetRequiredService<WaitingMachineSweeper>()
            .SweepOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, removed);
        Assert.False(await ExistsAsync(stale.Id));
        Assert.True(await ExistsAsync(fresh.Id));
        Assert.True(await ExistsAsync(approvedOnce.Id));
        Assert.Equal(HttpStatusCode.Unauthorized, (await stale.Agent.NextAsync(stale.Id, stale.PollToken)).StatusCode);
    }

    [Fact]
    public async Task AnOperatorRemovesAMachineNobodyApproved()
    {
        using SignedInClient operatorClient = await application.SignInAsync(DdtRoleNames.Operator);
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        using RegisteredMachine machine = await application.RegisterMachineAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"/api/machines/{machine.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await operatorClient.DeleteAsync($"/api/machines/{machine.Id}")).StatusCode);
        Assert.False(await ExistsAsync(machine.Id));
        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.Agent.NextAsync(machine.Id, machine.PollToken)).StatusCode);

        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        string subject = machine.Id.ToString("D");

        Assert.True(await database.AuditEvents.AnyAsync(
            e => e.SubjectId == subject && e.Action == AuditActions.MachineRemoved,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AMachineSomeoneApprovedIsNeverRemoved()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using RegisteredMachine machine = await application.RegisterMachineAsync();

        (await administrator.PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();
        (await machine.Agent.RegisterAsync(machine.Registration)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Conflict, (await administrator.DeleteAsync($"/api/machines/{machine.Id}")).StatusCode);
        Assert.True(await ExistsAsync(machine.Id));
    }

    [Fact]
    public async Task ARemovedRejectedMachineRegistersAgainAsNew()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using RegisteredMachine machine = await application.RegisterMachineAsync();

        (await administrator.PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();
        (await administrator.PostAsync($"/api/machines/{machine.Id}/reject")).EnsureSuccessStatusCode();

        AgentRegistrationResult stillRejected = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
            await machine.Agent.RegisterAsync(machine.Registration));

        Assert.Equal(MachineState.Rejected, stillRejected.State);
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"/api/machines/{machine.Id}")).StatusCode);
        Assert.False(await ExistsAsync(machine.Id));

        AgentRegistrationResult again = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
            await machine.Agent.RegisterAsync(machine.Registration));

        Assert.NotEqual(machine.Id, again.MachineId);
        Assert.Equal(MachineState.Pending, again.State);
    }

    // It waits on purpose, for a sign-in or a zero touch netboot, and removing it would silently drop the assignment.
    [Fact]
    public async Task AWaitingMachineWithAnAssignedImageIsNotRemoved()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string flood = TestRemoteAddress.Unique();

        using RegisteredMachine assigned = await application.RegisterMachineAsync(flood);
        using RegisteredMachine stray = await application.RegisterMachineAsync(flood);
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));

        await LastSeenAsync(assigned.Id, TimeSpan.FromMinutes(10));

        MachineSummary summary = await RegisteredMachine.ReadAsync<MachineSummary>(
            await administrator.PostAsync($"/api/machines/{assigned.Id}/deployments", new AssignImageRequest(image.Id, null)));

        Assert.Equal(MachineState.Pending, summary.State);

        HttpResponseMessage single = await administrator.DeleteAsync($"/api/machines/{assigned.Id}");

        Assert.Equal(HttpStatusCode.Conflict, single.StatusCode);
        Assert.Equal(
            "Only a rejected machine, or a waiting machine that was never approved and has no assigned image, can be removed.",
            await TestDatabase.TitleAsync(single));

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"/api/machines?waitingFrom={flood}")).StatusCode);
        Assert.False(await ExistsAsync(stray.Id));
        Assert.True(await ExistsAsync(assigned.Id));
        Assert.Equal(summary.Deployment!.Id, (await application.MachineAsync(assigned.Id)).ActiveDeploymentId);
    }

    [Fact]
    public async Task RemovesEverythingWaitingFromOneAddress()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string flood = TestRemoteAddress.Unique();

        using RegisteredMachine one = await application.RegisterMachineAsync(flood);
        using RegisteredMachine two = await application.RegisterMachineAsync(flood);
        using RegisteredMachine elsewhere = await application.RegisterMachineAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"/api/machines?waitingFrom={flood}")).StatusCode);
        Assert.False(await ExistsAsync(one.Id));
        Assert.False(await ExistsAsync(two.Id));
        Assert.True(await ExistsAsync(elsewhere.Id));

        IReadOnlyList<MachineSummary> machines = await RegisteredMachine.ReadAsync<IReadOnlyList<MachineSummary>>(
            await administrator.GetAsync("/api/machines"));

        MachineSummary remaining = Assert.Single(machines, m => m.Id == elsewhere.Id);
        Assert.False(remaining.EverApproved);
        Assert.NotNull(remaining.FirstSeenAddress);
    }
}
