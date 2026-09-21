// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using DDT.Server.Authentication;
using DDT.Server.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DDT.Server.Tests;

public sealed class ExternalAccountsTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    [Fact]
    public async Task ProvisionsTheAccountWithItsLinkAndItsRole()
    {
        using IServiceScope scope = application.Services.CreateScope();
        UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
        (DdtUser user, ExternalLoginInfo info) = Identity();

        IdentityResult result = await ExternalAccounts.ProvisionAsync(users, user, info, DdtRoleNames.Viewer);

        Assert.True(result.Succeeded);
        DdtUser? linked = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        Assert.NotNull(linked);
        Assert.Equal([DdtRoleNames.Viewer], await users.GetRolesAsync(linked));
    }

    [Fact]
    public async Task AFailedRoleAssignmentLeavesNoAccount()
    {
        using IServiceScope scope = application.Services.CreateScope();
        using RoleRefusingUserManager users = new(scope.ServiceProvider);
        (DdtUser user, ExternalLoginInfo info) = Identity();

        IdentityResult result = await ExternalAccounts.ProvisionAsync(users, user, info, DdtRoleNames.Viewer);

        Assert.False(result.Succeeded);
        Assert.Null(await users.FindByNameAsync(user.UserName!));
        Assert.Null(await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey));
    }

    // The identity's own account exists but its sign in stopped short, as at a second factor. A new account signed in
    // instead would carry the same identity past that factor.
    [Fact]
    public async Task AnIdentityThatBelongsToAnotherAccountLeavesNoAccount()
    {
        using IServiceScope scope = application.Services.CreateScope();
        UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
        (DdtUser user, ExternalLoginInfo info) = Identity();
        DdtUser owner = (await users.FindByNameAsync(await application.CreateUserAsync(DdtRoleNames.Operator)))!;
        Assert.True((await users.AddLoginAsync(owner, info)).Succeeded);

        IdentityResult result = await ExternalAccounts.ProvisionAsync(users, user, info, DdtRoleNames.Viewer);

        Assert.False(result.Succeeded);
        Assert.Null(await users.FindByNameAsync(user.UserName!));
        Assert.Equal(owner.Id, (await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey))?.Id);
    }

    [Theory]
    [InlineData("Viewer", true)]
    [InlineData("operator", true)]
    [InlineData("Administrator", true)]
    [InlineData("Viewers", false)]
    [InlineData("", false)]
    public void TheRoleForNewAccountsMustExist(string role, bool valid)
    {
        Assert.Equal(valid, OidcOptionsValidation.FindProblems(new OidcOptions { AutoProvisionRole = role }).Count == 0);
    }

    [Fact]
    public void AnUnknownRoleForNewAccountsStopsTheServer()
    {
        using SettingsApplication misconfigured = new(("DDT:Oidc:AutoProvisionRole", "Viewers"));

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() => misconfigured.CreateClient());

        Assert.Contains("DDT:Oidc:AutoProvisionRole: 'Viewers' is not a DDT role.", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("true", "Administrator", true)]
    [InlineData("false", "Administrator", false)]
    [InlineData("true", "Viewer", false)]
    public void NewAdministratorsAreAWarningAtEveryStart(string autoProvision, string role, bool warned)
    {
        using LoggedApplication host = new(
            ("DDT:Oidc:Enabled", "true"),
            ("DDT:Oidc:Authority", "https://idp.example"),
            ("DDT:Oidc:ClientId", "ddt"),
            ("DDT:Oidc:AutoProvision", autoProvision),
            ("DDT:Oidc:AutoProvisionRole", role));

        _ = host.Services;

        Assert.Equal(warned, host.Log.Logged(880, LogLevel.Warning));
    }

    private static (DdtUser User, ExternalLoginInfo Info) Identity()
    {
        string subject = Guid.NewGuid().ToString("N");
        ClaimsPrincipal principal = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "sso-" + subject)], "oidc"));

        DdtUser user = new()
        {
            UserName = principal.Identity!.Name,
            Source = AccountSource.Directory,
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        return (user, new ExternalLoginInfo(principal, OidcOptions.SchemeName, subject, "Single sign on"));
    }
}
