// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Server.Authentication;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace DDT.Server.Tests;

public sealed class WebApprovalSignInTests(WebApprovalApplication application) : IClassFixture<WebApprovalApplication>
{
    [Fact]
    public async Task SigningInOnlyRecordsWhoIsAtTheMachine()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using RegisteredMachine machine = await application.RegisterMachineAsync();

        Assert.Equal(AgentSignInStatus.Succeeded, (await machine.SignInAsync(operatorName)).Status);

        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(MachineState.Pending, next.State);
        Assert.Equal(operatorName, next.SignedInBy);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await machine.Agent.LogAsync(machine.Id, next.Token, new AgentLogBatch([new AgentLogLine(DateTimeOffset.UtcNow, AgentLogLevel.Information, "waiting")]))).StatusCode);
        Assert.Equal(AgentSignInStatus.AlreadyDecided, (await machine.SignInAsync(operatorName)).Status);

        SignedInClient administrator = await application.AdministratorAsync();
        IReadOnlyList<MachineSummary> machines = await RegisteredMachine.ReadAsync<IReadOnlyList<MachineSummary>>(
            await administrator.GetAsync("/api/machines"));

        Assert.False(Assert.Single(machines, m => m.Id == machine.Id).EverApproved);
    }

    [Fact]
    public async Task TheWebApprovalWaitsForASignInAtTheMachine()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using RegisteredMachine machine = await application.RegisterMachineAsync();
        SignedInClient administrator = await application.AdministratorAsync();

        HttpResponseMessage early = await administrator.PostAsync($"/api/machines/{machine.Id}/approve");

        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal(
            "Nobody has signed in at this machine yet.",
            (await early.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))?.Title);

        Assert.Equal(AgentSignInStatus.Succeeded, (await machine.SignInAsync(operatorName)).Status);

        MachineSummary approved = await RegisteredMachine.ReadAsync<MachineSummary>(
            await administrator.PostAsync($"/api/machines/{machine.Id}/approve"));

        Assert.Equal(MachineState.Approved, approved.State);
        Assert.Equal(operatorName, approved.SignedInBy);
        Assert.Equal(MachineState.Approved, (await machine.NextAsync()).State);
    }
}
