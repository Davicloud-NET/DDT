// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using DDT.Contracts.Authentication;
using DDT.Contracts.Users;
using DDT.Server.Authentication;
using DDT.Server.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

public sealed class ExternalSignInTests(ExternalSignInApplication application) : IClassFixture<ExternalSignInApplication>
{
    private const string SessionCookie = "ddt-auth";

    [Fact]
    public async Task AnUnknownIdentityGetsAnAccountWithItsLinkAndItsRole()
    {
        string subject = Guid.NewGuid().ToString("N");
        CookieContainer cookies = new();
        using HttpClient client = application.CreateDefaultClient(new CookieContainerHandler(cookies));

        using HttpResponseMessage completed = await SignInAsync(client, subject);

        Assert.Equal("/", completed.Headers.Location?.OriginalString);
        Assert.NotNull(cookies.GetCookies(client.BaseAddress!)[SessionCookie]);
        using IServiceScope scope = application.Services.CreateScope();
        UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
        DdtUser? user = await users.FindByLoginAsync(OidcOptions.SchemeName, subject);
        Assert.Equal(FakeOidcHandler.UserNameOf(subject), user?.UserName);
        Assert.Equal([DdtRoleNames.Operator], await users.GetRolesAsync(user!));
    }

    // DDT gave the role, until an administrator chooses one.
    [Fact]
    public async Task AProvisionedAccountIsExternalAndSaysItsRoleWasGiven()
    {
        string subject = Guid.NewGuid().ToString("N");
        CookieContainer cookies = new();
        using HttpClient client = application.CreateDefaultClient(new CookieContainerHandler(cookies));
        using HttpResponseMessage completed = await SignInAsync(client, subject);
        SignedInClient administrator = await application.AdministratorAsync();
        Guid id = await application.QueryAsync(database => database.Users
            .Where(u => u.UserName == FakeOidcHandler.UserNameOf(subject))
            .Select(u => u.Id)
            .SingleAsync(TestContext.Current.CancellationToken));

        UserView provisioned = await administrator.UserAsync(id);

        Assert.Equal(UserSource.External, provisioned.Source);
        Assert.Equal(DdtRoleNames.Operator, provisioned.Role);
        Assert.Equal(RoleSource.Provisioned, provisioned.RoleFrom);
        Assert.Equal("Single sign on", provisioned.ExternalProvider);
        Assert.False(provisioned.HasPassword);
        Assert.NotNull(provisioned.LastSignInUtc);

        UserView chosen = await RegisteredMachine.ReadAsync<UserView>(
            await administrator.PatchAsync($"{UserRequests.UsersApi}/{id}", new UpdateUserRequest(null, null, DdtRoleNames.Viewer)));

        Assert.Equal(RoleSource.Manual, chosen.RoleFrom);
        Assert.Equal(
            "This account signs in through single sign-on and has no password in DDT.",
            await TestDatabase.TitleAsync(await administrator.PostAsync($"{UserRequests.UsersApi}/{id}/reset-password")) is { } title
                ? title.Replace(FakeOidcHandler.UserNameOf(subject) + " signs in", "This account signs in", StringComparison.Ordinal)
                : null);
    }

    // The identity's own account stops at its second factor, and the sign-in page asks for the code as after a password.
    // A new account signed in instead would carry the same identity past that factor.
    [Fact]
    public async Task AnAccountWithASecondFactorFinishesTheSignInWithItsCode()
    {
        string subject = Guid.NewGuid().ToString("N");
        using IServiceScope scope = application.Services.CreateScope();
        UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
        DdtUser owner = await LinkedAccountAsync(users, subject);
        await users.ResetAuthenticatorKeyAsync(owner);
        await users.SetTwoFactorEnabledAsync(owner, true);
        string key = (await users.GetAuthenticatorKeyAsync(owner))!;
        CookieContainer cookies = new();
        using SignedInClient browser = new(application.CreateDefaultClient(new CookieContainerHandler(cookies)), cookies);

        using HttpResponseMessage completed = await SignInAsync(browser.Http, subject);

        Assert.Equal("/sign-in?step=two-factor", completed.Headers.Location?.OriginalString);
        Assert.Null(cookies.GetCookies(browser.Http.BaseAddress!)[SessionCookie]);
        Assert.Null(await users.FindByNameAsync(FakeOidcHandler.UserNameOf(subject)));
        Assert.Equal(owner.Id, (await users.FindByLoginAsync(OidcOptions.SchemeName, subject))?.Id);

        // The page knows no user name or password here, only the code. The log names the account all the same.
        string code = Totp.Code(key, DateTimeOffset.UtcNow);
        string wrong = ((int.Parse(code, CultureInfo.InvariantCulture) + 500_000) % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

        using HttpResponseMessage refused = await browser.PostAsync("/api/auth/login", new LoginRequest(string.Empty, string.Empty, wrong, null));

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Contains(application.Log.Messages, m => m.StartsWith($"Sign in failed for {owner.UserName} from ", StringComparison.Ordinal));

        using HttpResponseMessage login = await browser.PostAsync("/api/auth/login", new LoginRequest(string.Empty, string.Empty, code, null));

        Assert.Equal(LoginStatus.Succeeded, (await login.Content.ReadFromJsonAsync<LoginResponse>(TestJson.Options, TestContext.Current.CancellationToken))?.Status);
        Assert.Contains($"Sign in succeeded for {owner.UserName}", application.Log.Messages);
        Assert.NotNull(cookies.GetCookies(browser.Http.BaseAddress!)[SessionCookie]);

        CurrentUser? me = await (await browser.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<CurrentUser>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(owner.UserName, me?.UserName);
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("not-allowed")]
    public async Task ALockedOrDisabledAccountIsToldWhy(string error)
    {
        string subject = Guid.NewGuid().ToString("N");
        using IServiceScope scope = application.Services.CreateScope();
        UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
        DdtUser owner = await LinkedAccountAsync(users, subject);

        if (error == "locked")
        {
            await users.SetLockoutEndDateAsync(owner, DateTimeOffset.UtcNow.AddHours(1));
        }
        else
        {
            owner.IsDisabled = true;
            Assert.True((await users.UpdateAsync(owner)).Succeeded);
        }

        CookieContainer cookies = new();
        using HttpClient client = application.CreateDefaultClient(new CookieContainerHandler(cookies));

        using HttpResponseMessage completed = await SignInAsync(client, subject);

        Assert.Equal($"/sign-in?error={error}", completed.Headers.Location?.OriginalString);
        Assert.Null(cookies.GetCookies(client.BaseAddress!)[SessionCookie]);
        Assert.Null(await users.FindByNameAsync(FakeOidcHandler.UserNameOf(subject)));
    }

    private async Task<DdtUser> LinkedAccountAsync(UserManager<DdtUser> users, string subject)
    {
        DdtUser owner = (await users.FindByNameAsync(await application.CreateUserAsync(DdtRoleNames.Viewer)))!;
        Assert.True((await users.AddLoginAsync(owner, new UserLoginInfo(OidcOptions.SchemeName, subject, null))).Succeeded);

        return owner;
    }

    private static async Task<HttpResponseMessage> SignInAsync(HttpClient client, string subject)
    {
        using HttpResponseMessage challenged = await client.GetAsync(
            new Uri($"/api/auth/external/start?{FakeOidcHandler.SubjectParameter}={subject}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, challenged.StatusCode);

        return await client.GetAsync(challenged.Headers.Location, TestContext.Current.CancellationToken);
    }
}
