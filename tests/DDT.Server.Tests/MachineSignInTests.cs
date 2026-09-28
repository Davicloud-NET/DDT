// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Net;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

public sealed class MachineSignInTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private const string WrongPassword = "Not the right password 7";

    [Fact]
    public async Task SigningInAtTheMachineApprovesIt()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using RegisteredMachine machine = await application.RegisterMachineAsync();

        Assert.Equal(AgentSignInStatus.Succeeded, (await machine.SignInAsync(operatorName)).Status);

        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(MachineState.Approved, next.State);
        Assert.Equal(operatorName, next.SignedInBy);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await machine.Agent.LogAsync(machine.Id, next.Token, new AgentLogBatch([new AgentLogLine(DateTimeOffset.UtcNow, AgentLogLevel.Information, "approved")]))).StatusCode);

        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        Guid operatorId = (await scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>().FindByNameAsync(operatorName))!.Id;
        string subject = machine.Id.ToString("D");

        List<AuditEvent> events = await database.AuditEvents
            .Where(e => e.SubjectId == subject)
            .OrderBy(e => e.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([AuditActions.MachineRegistered, AuditActions.MachineSignedIn, AuditActions.MachineApproved], events.Select(e => e.Action));
        Assert.All(events.Skip(1), e =>
        {
            Assert.Equal(operatorId, e.ActorUserId);
            Assert.Equal(machine.Id, e.ActorMachineId);
            Assert.Equal(operatorName, e.ActorName);
        });
    }

    [Fact]
    public async Task WrongPasswordsLockTheAccount()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using RegisteredMachine machine = await application.RegisterMachineAsync();

        for (int attempt = 1; attempt < 5; attempt++)
        {
            Assert.Equal(AgentSignInStatus.Failed, (await machine.SignInAsync(operatorName, WrongPassword)).Status);
        }

        Assert.Equal(AgentSignInStatus.LockedOut, (await machine.SignInAsync(operatorName, WrongPassword)).Status);
        Assert.Equal(AgentSignInStatus.LockedOut, (await machine.SignInAsync(operatorName)).Status);
        Assert.Equal(MachineState.Pending, (await machine.NextAsync()).State);
    }

    [Fact]
    public async Task UnknownAndDisabledAccountsFail()
    {
        string disabled = await application.CreateUserAsync(DdtRoleNames.Operator);

        using (IServiceScope scope = application.Services.CreateScope())
        {
            UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
            DdtUser user = (await users.FindByNameAsync(disabled))!;
            user.IsDisabled = true;
            await users.UpdateAsync(user);
        }

        using RegisteredMachine machine = await application.RegisterMachineAsync();

        Assert.Equal(AgentSignInStatus.Failed, (await machine.SignInAsync($"nobody-{Guid.NewGuid():N}")).Status);
        Assert.Equal(AgentSignInStatus.Failed, (await machine.SignInAsync(disabled)).Status);
        Assert.Equal(MachineState.Pending, (await machine.NextAsync()).State);
    }

    [Fact]
    public async Task AViewerCannotAuthorizeAMachine()
    {
        string viewer = await application.CreateUserAsync(DdtRoleNames.Viewer);
        using RegisteredMachine machine = await application.RegisterMachineAsync();

        Assert.Equal(AgentSignInStatus.NotPermitted, (await machine.SignInAsync(viewer)).Status);
        Assert.Equal(MachineState.Pending, (await machine.NextAsync()).State);
    }

    [Fact]
    public async Task ATwoFactorAccountNeedsItsCodeAndStartsCountingAfresh()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        string key = await EnableTwoFactorAsync(operatorName);

        using RegisteredMachine machine = await application.RegisterMachineAsync();
        string code = Totp.Code(key, DateTimeOffset.UtcNow);

        Assert.Equal(AgentSignInStatus.Failed, (await machine.SignInAsync(operatorName, WrongPassword)).Status);
        Assert.Equal(AgentSignInStatus.RequiresTwoFactor, (await machine.SignInAsync(operatorName)).Status);
        Assert.Equal(AgentSignInStatus.Failed, (await machine.SignInAsync(operatorName, code: WrongCode(code))).Status);
        Assert.Equal(2, await AccessFailedCountAsync(operatorName));
        Assert.Equal(MachineState.Pending, (await machine.NextAsync()).State);
        Assert.Equal(AgentSignInStatus.Succeeded, (await machine.SignInAsync(operatorName, code: code)).Status);
        Assert.Equal(MachineState.Approved, (await machine.NextAsync()).State);
        Assert.Equal(0, await AccessFailedCountAsync(operatorName));
    }

    [Fact]
    public async Task WrongCodesLockTheAccount()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        string key = await EnableTwoFactorAsync(operatorName);

        using RegisteredMachine machine = await application.RegisterMachineAsync();
        string code = Totp.Code(key, DateTimeOffset.UtcNow);

        for (int attempt = 1; attempt < 5; attempt++)
        {
            Assert.Equal(AgentSignInStatus.Failed, (await machine.SignInAsync(operatorName, code: WrongCode(code))).Status);
        }

        Assert.Equal(AgentSignInStatus.LockedOut, (await machine.SignInAsync(operatorName, code: WrongCode(code))).Status);
        Assert.Equal(AgentSignInStatus.LockedOut, (await machine.SignInAsync(operatorName, code: code)).Status);
        Assert.Equal(MachineState.Pending, (await machine.NextAsync()).State);
    }

    [Fact]
    public async Task ADecidedMachineTakesNoFurtherSignIn()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using RegisteredMachine machine = await application.RegisterMachineAsync();

        Assert.Equal(AgentSignInStatus.Succeeded, (await machine.SignInAsync(operatorName)).Status);
        Assert.Equal(AgentSignInStatus.AlreadyDecided, (await machine.SignInAsync(operatorName)).Status);
    }

    [Fact]
    public async Task RegisteringAgainForgetsTheSignIn()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using RegisteredMachine machine = await application.RegisterMachineAsync();

        Assert.Equal(AgentSignInStatus.Succeeded, (await machine.SignInAsync(operatorName)).Status);

        AgentRegistrationResult again = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
            await machine.Agent.RegisterAsync(machine.Registration));

        Assert.Equal(MachineState.Pending, again.State);
        Assert.Null(again.SignedInBy);
        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.Agent.SignInAsync(machine.Id, machine.PollToken, new AgentSignInRequest(operatorName, DdtApplication.Password, null))).StatusCode);

        // It's waiting again, but someone vouched for it once. Its log must survive, so nothing may remove it.
        SignedInClient administrator = await application.AdministratorAsync();
        IReadOnlyList<MachineSummary> machines = await RegisteredMachine.ReadAsync<IReadOnlyList<MachineSummary>>(
            await administrator.GetAsync("/api/machines"));

        Assert.True(Assert.Single(machines, m => m.Id == machine.Id).EverApproved);
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.DeleteAsync($"/api/machines/{machine.Id}")).StatusCode);

        AgentSignInResult signedInAgain = await RegisteredMachine.ReadAsync<AgentSignInResult>(
            await machine.Agent.SignInAsync(machine.Id, again.Token!, new AgentSignInRequest(operatorName, DdtApplication.Password, null)));

        Assert.Equal(AgentSignInStatus.Succeeded, signedInAgain.Status);
    }

    [Fact]
    public async Task RefusesATokenOfAnotherMachineAndEmptyCredentials()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using RegisteredMachine one = await application.RegisterMachineAsync();
        using RegisteredMachine two = await application.RegisterMachineAsync();

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await one.Agent.SignInAsync(two.Id, one.PollToken, new AgentSignInRequest(operatorName, DdtApplication.Password, null))).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await one.Agent.SignInAsync(one.Id, one.PollToken, new AgentSignInRequest(" ", string.Empty, null))).StatusCode);
    }

    private static string WrongCode(string code) =>
        ((int.Parse(code, CultureInfo.InvariantCulture) + 500_000) % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    private async Task<string> EnableTwoFactorAsync(string userName)
    {
        using IServiceScope scope = application.Services.CreateScope();
        UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
        DdtUser user = (await users.FindByNameAsync(userName))!;

        await users.ResetAuthenticatorKeyAsync(user);
        await users.SetTwoFactorEnabledAsync(user, true);

        return (await users.GetAuthenticatorKeyAsync(user))!;
    }

    private async Task<int> AccessFailedCountAsync(string userName)
    {
        using IServiceScope scope = application.Services.CreateScope();
        UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();

        return await users.GetAccessFailedCountAsync((await users.FindByNameAsync(userName))!);
    }
}
