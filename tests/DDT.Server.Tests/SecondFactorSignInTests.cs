// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using DDT.Contracts.Authentication;
using DDT.Server.Authentication;
using DDT.Server.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// Uses ExternalSignInApplication for the log it records.
// The code step is the same after a password and after OpenID Connect.
public sealed class SecondFactorSignInTests(ExternalSignInApplication application) : IClassFixture<ExternalSignInApplication>
{
    private const string SessionCookie = "ddt-auth";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnAccountDisabledAfterItsPasswordIsRefusedAtItsCode(bool recovery)
    {
        string userName = await application.CreateUserAsync(DdtRoleNames.Viewer);
        (Guid id, string key, string recoveryCode) = await SecondFactorAsync(userName);
        CookieContainer cookies = new();
        using SignedInClient browser = new(application.CreateDefaultClient(new CookieContainerHandler(cookies)), cookies);

        Assert.Equal(
            LoginStatus.RequiresTwoFactor,
            (await RegisteredMachine.ReadAsync<LoginResponse>(await browser.PostAsync("/api/auth/login", new LoginRequest(userName, DdtApplication.Password, null, null)))).Status);

        await SetDisabledAsync(id, true);
        LoginRequest code = recovery
            ? new LoginRequest(string.Empty, string.Empty, null, recoveryCode)
            : new LoginRequest(string.Empty, string.Empty, Totp.Code(key, DateTimeOffset.UtcNow), null);

        using HttpResponseMessage refused = await browser.PostAsync("/api/auth/login", code);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Contains(application.Log.Messages, m => m.StartsWith($"Sign in failed for {userName} from ", StringComparison.Ordinal));
        Assert.Null(cookies.GetCookies(browser.Http.BaseAddress!)[SessionCookie]);

        // Only the flag refused it, and the refusal didn't use anything up.
        // Once the account is enabled again, the same code signs in.
        await SetDisabledAsync(id, false);
        using HttpResponseMessage login = await browser.PostAsync("/api/auth/login", code);

        Assert.Equal(LoginStatus.Succeeded, (await login.Content.ReadFromJsonAsync<LoginResponse>(TestJson.Options, TestContext.Current.CancellationToken))?.Status);
    }

    private async Task<(Guid Id, string Key, string RecoveryCode)> SecondFactorAsync(string userName)
    {
        using IServiceScope scope = application.Services.CreateScope();
        UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
        DdtUser user = (await users.FindByNameAsync(userName))!;

        await users.ResetAuthenticatorKeyAsync(user);
        await users.SetTwoFactorEnabledAsync(user, true);
        IEnumerable<string>? recoveryCodes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 1);

        return (user.Id, (await users.GetAuthenticatorKeyAsync(user))!, Assert.Single(recoveryCodes!));
    }

    private async Task SetDisabledAsync(Guid userId, bool disabled)
    {
        using IServiceScope scope = application.Services.CreateScope();
        UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
        DdtUser user = (await users.FindByIdAsync(userId.ToString("D")))!;

        user.IsDisabled = disabled;
        Assert.True((await users.UpdateAsync(user)).Succeeded);
    }
}
