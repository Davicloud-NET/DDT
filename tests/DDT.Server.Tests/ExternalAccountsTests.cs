// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using DDT.Contracts.Authentication;
using DDT.Server.Authentication;
using DDT.Server.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
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

    // The identity's own account exists, but its sign-in stopped early, like at a second factor.
    // Signing in a new account instead would carry the same identity past that factor.
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
    [InlineData("Administrator", false)]
    [InlineData("Viewers", false)]
    [InlineData("", false)]
    public void TheRoleForNewAccountsMustExistAndNotBeAdministrator(string role, bool valid)
    {
        Assert.Equal(valid, OidcOptionsValidation.FindProblems(new OidcOptions { AutoProvisionRole = role }).Count == 0);
    }

    [Fact]
    public void TheGroupMapNeedsRolesDdtHasAndAClaimToReadThemFrom()
    {
        OidcOptions options = new() { GroupsClaim = " " };
        options.GroupRoleMap["DDT-Admins"] = "Administrator";
        options.GroupRoleMap["ddt-owners"] = "Owner";

        Assert.Equal(
            ["GroupRoleMap:ddt-owners", "GroupsClaim"],
            OidcOptionsValidation.FindProblems(options).Select(problem => problem.Field));
        Assert.Equal("Administrator", options.GroupRoleMap["ddt-admins"]);
    }

    [Fact]
    public void AnUnknownRoleForNewAccountsStopsTheServer()
    {
        using SettingsApplication misconfigured = new(("DDT:Oidc:AutoProvisionRole", "Viewers"));

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() => misconfigured.CreateClient());

        Assert.Contains("DDT:Oidc:AutoProvisionRole: 'Viewers' is not a DDT role.", refusal.Message, StringComparison.Ordinal);
    }

    // Otherwise every identity the provider signs in that DDT hasn't seen yet would become an administrator.
    [Fact]
    public void AnAdministratorRoleForNewAccountsStopsTheServer()
    {
        using SettingsApplication misconfigured = new(
            ("DDT:Oidc:Enabled", "true"),
            ("DDT:Oidc:Authority", "https://idp.example"),
            ("DDT:Oidc:ClientId", "ddt"),
            ("DDT:Oidc:AutoProvision", "true"),
            ("DDT:Oidc:AutoProvisionRole", "Administrator"));

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() => misconfigured.CreateClient());

        Assert.Contains("DDT:Oidc:AutoProvisionRole: Administrator would make every identity", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSignInPageIsToldWhichProvidersThereAre()
    {
        using SettingsApplication off = new();
        using SettingsApplication on = new(
            ("DDT:Oidc:Enabled", "true"),
            ("DDT:Oidc:Authority", "https://idp.example"),
            ("DDT:Oidc:ClientId", "ddt"),
            ("DDT:Oidc:DisplayName", "Contoso"));
        using HttpClient offClient = off.CreateClient();
        using HttpClient onClient = on.CreateClient();

        Assert.Empty(await RegisteredMachine.ReadAsync<List<ExternalProvider>>(
            await offClient.GetAsync(new Uri("/api/auth/external/providers", UriKind.Relative), TestContext.Current.CancellationToken)));
        Assert.Equal(
            [new ExternalProvider(OidcOptions.SchemeName, "Contoso")],
            await RegisteredMachine.ReadAsync<List<ExternalProvider>>(
                await onClient.GetAsync(new Uri("/api/auth/external/providers", UriKind.Relative), TestContext.Current.CancellationToken)));
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
