// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Settings;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// The fresh proof of identity the fields that grant roles or trust need: a password, checked like a sign-in, and a
// token bound to the account and its security stamp for 5 minutes.
public sealed class ReauthenticationTests(ManualClockApplication application) : IClassFixture<ManualClockApplication>
{
    [Fact]
    public async Task AWrongPasswordGetsNoToken()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        HttpResponseMessage response = await administrator.ReauthenticateAsync("Not the password 1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("password", (await SettingsRequests.ProblemsAsync(response)).Errors.Keys);
    }

    [Fact]
    public async Task ATokenLastsFiveMinutes()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ReauthenticationToken token = await RegisteredMachine.ReadAsync<ReauthenticationToken>(await administrator.ReauthenticateAsync());

        Assert.Equal(application.Clock.GetUtcNow() + TimeSpan.FromMinutes(5), token.ExpiresUtc);
        Assert.Equal(HttpStatusCode.OK, (await SaveZeroTouchAsync(administrator, "10.230.0.0/16", token.Token)).StatusCode);

        application.Clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(HttpStatusCode.Forbidden, (await SaveZeroTouchAsync(administrator, "10.231.0.0/16", token.Token)).StatusCode);
    }

    [Fact]
    public async Task ATokenServesOnlyTheAccountItWasIssuedTo()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SignedInClient other = await application.SignInAsync(DdtRoleNames.Administrator);
        string token = await other.TokenAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await SaveZeroTouchAsync(administrator, "10.232.0.0/16", token)).StatusCode);
    }

    // A new password, a new authenticator or signing out everywhere changes the stamp.
    [Fact]
    public async Task ANewSecurityStampEndsTheToken()
    {
        SignedInClient administrator = await application.SignInAsync(DdtRoleNames.Administrator);
        string token = await administrator.TokenAsync();

        await ChangeUserAsync(administrator, (users, user) => users.UpdateSecurityStampAsync(user));

        Assert.Equal(HttpStatusCode.Forbidden, (await SaveZeroTouchAsync(administrator, "10.233.0.0/16", token)).StatusCode);
    }

    // Such an account cannot prove who it is again, so it cannot change these fields at all.
    [Fact]
    public async Task AnAccountWithoutAPasswordCannotReauthenticate()
    {
        SignedInClient administrator = await application.SignInAsync(DdtRoleNames.Administrator);

        await ChangeUserAsync(administrator, (users, user) => users.RemovePasswordAsync(user));

        HttpResponseMessage response = await administrator.ReauthenticateAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.StartsWith("This account signs in without a password DDT can check", await TestDatabase.TitleAsync(response), StringComparison.Ordinal);
    }

    // A script's token cannot prove that a person is there.
    [Fact]
    public async Task AnApiTokenCannotReauthenticate()
    {
        string userName = await application.CreateUserAsync(DdtRoleNames.Administrator);
        (_, string secret) = await application.SeedTokenAsync(userName, DdtRoleNames.Administrator);
        using HttpClient script = application.TokenClient(secret, TestRemoteAddress.Unique());

        HttpResponseMessage response = await script.PostJsonAsync("/api/settings/reauthenticate", new ReauthenticateRequest(DdtApplication.Password, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> SaveZeroTouchAsync(SignedInClient client, string network, string token)
    {
        SettingsSectionView<MachineSettings> loaded = await client.SectionAsync<MachineSettings>(SettingsSectionNames.Machines);

        return await client.SaveAsync(
            SettingsSectionNames.Machines,
            loaded.Version,
            loaded.Values with { ZeroTouchNetworks = [network] },
            new(Reauthentication: token));
    }

    private async Task ChangeUserAsync(SignedInClient client, Func<UserManager<DdtUser>, DdtUser, Task<IdentityResult>> change)
    {
        CurrentUserName current = await RegisteredMachine.ReadAsync<CurrentUserName>(await client.GetAsync("/api/auth/me"));
        using IServiceScope scope = application.Services.CreateScope();
        UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
        DdtUser user = (await users.FindByNameAsync(current.UserName))!;

        Assert.True((await change(users, user)).Succeeded);
    }

    private sealed record CurrentUserName(string UserName);
}
