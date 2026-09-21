// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using DDT.Contracts.Agents;
using DDT.Contracts.Authentication;
using DDT.Contracts.Machines;
using DDT.Server.Authentication;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Xunit;

namespace DDT.Server.Tests;

public sealed class DirectorySignInTests(DirectoryApplication application) : IClassFixture<DirectoryApplication>
{
    [Fact]
    public async Task ADirectoryOperatorSignsInAtTheMachine()
    {
        using RegisteredMachine machine = await application.RegisterMachineAsync();

        Assert.Equal(AgentSignInStatus.Succeeded, (await machine.SignInAsync($"tech-{Guid.NewGuid():N}")).Status);
        Assert.Equal(MachineState.Approved, (await machine.NextAsync()).State);
    }

    [Fact]
    public async Task ADirectoryOperatorStillSignsInOnTheWeb()
    {
        string userName = $"tech-{Guid.NewGuid():N}";
        CookieContainer cookies = new();
        using SignedInClient browser = new(application.CreateDefaultClient(new CookieContainerHandler(cookies)), cookies);

        LoginResponse? login = await (await browser.PostAsync("/api/auth/login", new LoginRequest(userName, DdtApplication.Password, null, null)))
            .Content.ReadFromJsonAsync<LoginResponse>(TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(LoginStatus.Succeeded, login?.Status);

        CurrentUser me = await RegisteredMachine.ReadAsync<CurrentUser>(await browser.GetAsync("/api/auth/me"));

        Assert.Equal(userName, me.UserName);
        Assert.Contains(DdtRoleNames.Operator, me.Roles);
    }

    [Fact]
    public async Task ADirectoryNameTheAccountStoreRefusesFailsCleanly()
    {
        using RegisteredMachine machine = await application.RegisterMachineAsync();

        // Identity's default user names have no apostrophe, so this account can never be stored.
        Assert.Equal(AgentSignInStatus.Failed, (await machine.SignInAsync("o'brien")).Status);
        Assert.Equal(MachineState.Pending, (await machine.NextAsync()).State);
    }

    [Fact]
    public async Task ARegistrationDuringTheCheckDoesNotReceiveTheApproval()
    {
        using RegisteredMachine machine = await application.RegisterMachineAsync();
        AgentRegistrationResult? intruder = null;

        // Someone else presents this machine's UUID and MAC while the directory is still checking the password.
        application.Ldap.DuringBind = async () =>
            intruder = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
                await machine.Agent.RegisterAsync(machine.Registration));

        HttpResponseMessage response = await machine.Agent.SignInAsync(
            machine.Id,
            machine.PollToken,
            new AgentSignInRequest($"tech-{Guid.NewGuid():N}", DdtApplication.Password, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(intruder);

        AgentNextResult next = await RegisteredMachine.ReadAsync<AgentNextResult>(
            await machine.Agent.NextAsync(machine.Id, intruder.Token!));

        Assert.Equal(MachineState.Pending, next.State);
        Assert.Null(next.SignedInBy);
    }
}
