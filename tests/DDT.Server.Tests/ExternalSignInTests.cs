// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Server.Authentication;
using DDT.Server.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
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

    // The identity's own account stops at its second factor. A new account signed in instead would carry the same
    // identity past that factor.
    [Fact]
    public async Task AnIdentityWhoseAccountNeedsASecondFactorGetsNoAccountOfItsOwn()
    {
        string subject = Guid.NewGuid().ToString("N");
        using IServiceScope scope = application.Services.CreateScope();
        UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
        DdtUser owner = (await users.FindByNameAsync(await application.CreateUserAsync(DdtRoleNames.Viewer)))!;
        await users.ResetAuthenticatorKeyAsync(owner);
        await users.SetTwoFactorEnabledAsync(owner, true);
        Assert.True((await users.AddLoginAsync(owner, new UserLoginInfo(OidcOptions.SchemeName, subject, null))).Succeeded);
        CookieContainer cookies = new();
        using HttpClient client = application.CreateDefaultClient(new CookieContainerHandler(cookies));

        using HttpResponseMessage completed = await SignInAsync(client, subject);

        Assert.Equal("/sign-in?error=provision", completed.Headers.Location?.OriginalString);
        Assert.Null(cookies.GetCookies(client.BaseAddress!)[SessionCookie]);
        Assert.Null(await users.FindByNameAsync(FakeOidcHandler.UserNameOf(subject)));
        Assert.Equal(owner.Id, (await users.FindByLoginAsync(OidcOptions.SchemeName, subject))?.Id);
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
