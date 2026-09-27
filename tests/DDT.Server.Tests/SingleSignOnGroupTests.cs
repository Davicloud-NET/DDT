// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Claims;
using System.Text.Json;
using DDT.Contracts.Users;
using DDT.Server.Authentication;
using DDT.Server.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DDT.Server.Tests.GroupMappedSignInApplication;

namespace DDT.Server.Tests;

public sealed class SingleSignOnGroupTests(GroupMappedSignInApplication application) : IClassFixture<GroupMappedSignInApplication>
{
    private const string SessionCookie = "ddt-auth";

    private static string NewSubject() => Guid.NewGuid().ToString("N");

    // Where the sign-in ended, and whether it left a session.
    private async Task<(string? Location, bool SignedIn)> SignInAsync(string subject, string groups = "", string groupsArray = "")
    {
        CookieContainer cookies = new();
        using HttpClient client = application.CreateDefaultClient(new CookieContainerHandler(cookies));
        using HttpResponseMessage challenged = await client.GetAsync(
            new Uri(
                $"/api/auth/external/start?{FakeOidcHandler.SubjectParameter}={subject}&{FakeOidcHandler.GroupsParameter}={Uri.EscapeDataString(groups)}" +
                $"&{FakeOidcHandler.GroupsArrayParameter}={Uri.EscapeDataString(groupsArray)}",
                UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, challenged.StatusCode);

        using HttpResponseMessage completed = await client.GetAsync(challenged.Headers.Location, TestContext.Current.CancellationToken);

        return (completed.Headers.Location?.OriginalString, cookies.GetCookies(client.BaseAddress!)[SessionCookie] is not null);
    }

    private async Task<(DdtUser? User, IList<string> Roles)> AccountAsync(string subject)
    {
        using IServiceScope scope = application.Services.CreateScope();
        UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
        DdtUser? user = await users.FindByLoginAsync(OidcOptions.SchemeName, subject);

        return (user, user is null ? [] : await users.GetRolesAsync(user));
    }

    [Fact]
    public async Task TheHighestMappedRoleComesBeforeTheRoleForNewAccounts()
    {
        string subject = NewSubject();

        Assert.Equal(("/", true), await SignInAsync(subject, $"{Operators},{Administrators},unrelated"));

        (DdtUser? user, IList<string> roles) = await AccountAsync(subject);
        Assert.Equal([DdtRoleNames.Administrator], roles);

        UserView view = await (await application.AdministratorAsync()).UserAsync(user!.Id);
        Assert.Equal(UserSource.External, view.Source);
        Assert.Equal(RoleSource.SingleSignOnGroups, view.RoleFrom);
    }

    // Entra ID sends object ids, Keycloak paths; one claim each, or one claim holding the array.
    [Theory]
    [InlineData(EntraOperators, "", DdtRoleNames.Operator)]
    [InlineData(ViewersPath, "", DdtRoleNames.Viewer)]
    [InlineData("", "DDT-OPERATORS,unrelated", DdtRoleNames.Operator)]
    public async Task ReadsTheGroupsAsTheProviderSendsThem(string groups, string groupsArray, string role)
    {
        string subject = NewSubject();

        Assert.Equal(("/", true), await SignInAsync(subject, groups, groupsArray));
        Assert.Equal([role], (await AccountAsync(subject)).Roles);
    }

    [Fact]
    public async Task AnIdentityInNoneOfTheGroupsIsRefusedAndGetsNoAccount()
    {
        string subject = NewSubject();

        Assert.Equal(("/sign-in?error=no-role", false), await SignInAsync(subject, "unrelated"));
        Assert.Null((await AccountAsync(subject)).User);
    }

    [Fact]
    public async Task TheGroupsDecideAtEachSignInAndTakeTheRoleAway()
    {
        string subject = NewSubject();
        SignedInClient administrator = await application.AdministratorAsync();

        Assert.Equal(("/", true), await SignInAsync(subject, Administrators));
        Assert.Equal(("/", true), await SignInAsync(subject, ViewersPath));
        Assert.Equal([DdtRoleNames.Viewer], (await AccountAsync(subject)).Roles);

        Guid id = (await AccountAsync(subject)).User!.Id;
        using HttpResponseMessage refused = await administrator.PatchAsync($"{UserRequests.UsersApi}/{id}", new UpdateUserRequest(null, null, DdtRoleNames.Operator));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(
            $"The role of {FakeOidcHandler.UserNameOf(subject)} comes from its single sign-on groups through DDT:Oidc:GroupRoleMap, at each sign-in. " +
            "Change its groups at the provider, or the map.",
            await TestDatabase.TitleAsync(refused));

        Assert.Equal(("/sign-in?error=no-role", false), await SignInAsync(subject));
        Assert.Empty((await AccountAsync(subject)).Roles);
        UserView view = await administrator.UserAsync(id);
        Assert.Null(view.Role);
        Assert.Equal(RoleSource.SingleSignOnGroups, view.RoleFrom);
    }

    // An administrator gave the local account its role, and the groups of an identity linked to it do not take it away.
    [Fact]
    public async Task ALinkedLocalAccountKeepsItsRole()
    {
        string subject = NewSubject();

        using (IServiceScope scope = application.Services.CreateScope())
        {
            UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
            DdtUser owner = (await users.FindByNameAsync(await application.CreateUserAsync(DdtRoleNames.Operator)))!;
            Assert.True((await users.AddLoginAsync(owner, new UserLoginInfo(OidcOptions.SchemeName, subject, null))).Succeeded);
        }

        Assert.Equal(("/", true), await SignInAsync(subject, "unrelated"));
        Assert.Equal([DdtRoleNames.Operator], (await AccountAsync(subject)).Roles);
    }

    [Fact]
    public void ReadsOneClaimPerGroupAndClaimsHoldingAnArray()
    {
        ClaimsPrincipal principal = new(new ClaimsIdentity(
        [
            new Claim("groups", "a"),
            new Claim("groups", "[\"b\",\"c\",null]"),
            new Claim("groups", " a "),
            new Claim("roles", "d"),
            new Claim("groups", "[not json"),
        ]));

        Assert.Equal(["a", "b", "c", "[not json"], SingleSignOnGroups.Read(principal, "groups"));
        Assert.Empty(SingleSignOnGroups.Read(principal, ""));
    }

    [Fact]
    public void TakesTheGroupsFromTheUserInformationWhenTheTokenHasNone()
    {
        ClaimsIdentity identity = new([new Claim("groups", "a")]);
        using JsonDocument array = JsonDocument.Parse("{ \"sub\": \"x\", \"groups\": [\"a\", \"b\", 3] }");
        using JsonDocument single = JsonDocument.Parse("{ \"sub\": \"x\", \"memberOf\": \"c\" }");

        SingleSignOnGroups.CopyFromUserInformation(array.RootElement, identity, "groups", "issuer");
        SingleSignOnGroups.CopyFromUserInformation(single.RootElement, identity, "memberOf", "issuer");

        Assert.Equal(["a", "b"], identity.FindAll("groups").Select(claim => claim.Value));
        Assert.Equal(["c"], identity.FindAll("memberOf").Select(claim => claim.Value));
    }
}
