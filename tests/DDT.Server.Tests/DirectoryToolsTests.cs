// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Threading.Channels;
using DDT.Contracts.Agents;
using DDT.Contracts.Authentication;
using DDT.Contracts.Users;
using DDT.Server.Authentication;
using DDT.Server.Live;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DDT.Server.Tests.FakeLdapAuthenticator;

namespace DDT.Server.Tests;

public sealed class DirectoryToolsTests(DirectoryApplication application) : IClassFixture<DirectoryApplication>
{
    private const string Directory = "/api/directory";

    private static Task<T> ReadAsync<T>(HttpResponseMessage response) => RegisteredMachine.ReadAsync<T>(response);

    private static string NewUser() => $"tech-{Guid.NewGuid():N}";

    private async Task<DirectoryCheck> CheckAsync(string userName) =>
        await ReadAsync<DirectoryCheck>(await (await application.AdministratorAsync()).PostAsync($"{Directory}/check", new DirectoryCheckRequest(userName)));

    private Task<bool> AccountExistsAsync(string userName) =>
        application.QueryAsync(database => database.Users.AnyAsync(u => u.UserName == userName, TestContext.Current.CancellationToken));

    [Fact]
    public async Task ShowsTheMapWithTheNamesTheDirectoryHas()
    {
        DirectoryView view = await ReadAsync<DirectoryView>(await (await application.AdministratorAsync()).GetAsync(Directory));

        Assert.True(view.Enabled);
        Assert.Equal(DirectoryApplication.Host, view.Host);
        Assert.Equal(DirectoryApplication.BaseDn, view.BaseDn);
        Assert.Equal(
            [
                new DirectoryGroupMapping(AdministratorsGroup, "DDT Administrators", DdtRoleNames.Administrator),
                new DirectoryGroupMapping(OperatorsGroup, "DDT Operators", DdtRoleNames.Operator),
                new DirectoryGroupMapping(ViewersGroup, "DDT Viewers", DdtRoleNames.Viewer),
                new DirectoryGroupMapping(RetiredGroup, null, DdtRoleNames.Viewer),
            ],
            view.GroupRoleMap);
    }

    [Fact]
    public async Task FindsGroupsByTheirNameStartFirst()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        List<DirectoryGroup> found = await ReadAsync<List<DirectoryGroup>>(await administrator.GetAsync($"{Directory}/groups?query=oper"));

        Assert.Equal([UnmappedGroup, OperatorsGroup], found.Select(group => group.DistinguishedName));
        Assert.Equal(new DirectoryGroup(OperatorsGroup, "DDT Operators", "Deploy machines"), found[1]);
        Assert.Single(await ReadAsync<List<DirectoryGroup>>(await administrator.GetAsync($"{Directory}/groups?query=ddt&limit=1")));
        Assert.Equal(4, (await ReadAsync<List<DirectoryGroup>>(await administrator.GetAsync($"{Directory}/groups"))).Count);
    }

    // No password, no account and no cookie. The check only says what a sign-in would give, and why.
    [Fact]
    public async Task ChecksWhatASignInWouldGiveWithoutSigningAnyoneIn()
    {
        string userName = NewUser();
        application.Ldap.Groups[userName] = [ViewersGroup, UnmappedGroup, OperatorsGroup];

        using HttpResponseMessage response = await (await application.AdministratorAsync()).PostAsync($"{Directory}/check", new DirectoryCheckRequest(userName));
        DirectoryCheck check = await ReadAsync<DirectoryCheck>(response);

        Assert.True(check.Found);
        Assert.Equal(DistinguishedNameOf(userName), check.DistinguishedName);
        Assert.Equal([ViewersGroup, UnmappedGroup, OperatorsGroup], check.Groups);
        Assert.Equal([new DirectoryGroupMatch(ViewersGroup, DdtRoleNames.Viewer), new DirectoryGroupMatch(OperatorsGroup, DdtRoleNames.Operator)], check.Matches);
        Assert.Equal(DdtRoleNames.Operator, check.Role);
        Assert.Equal($"{userName} is in 2 mapped groups and gets the highest role they give, Operator from operators.", check.Message);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.False(await AccountExistsAsync(userName));
    }

    [Fact]
    public async Task SaysWhyASignInWouldBeRefused()
    {
        string unmapped = NewUser();
        application.Ldap.Groups[unmapped] = [UnmappedGroup];
        string local = await application.CreateUserAsync(DdtRoleNames.Viewer);

        DirectoryCheck missing = await CheckAsync(MissingPrefix + "x");
        Assert.False(missing.Found);
        Assert.Null(missing.Role);
        Assert.Equal($"No entry under {DirectoryApplication.BaseDn} matches {MissingPrefix}x with the user filter, so a sign-in with it is refused.", missing.Message);

        Assert.StartsWith("More than one entry", (await CheckAsync(TwinPrefix + "x")).Message, StringComparison.Ordinal);

        DirectoryCheck none = await CheckAsync(unmapped);
        Assert.True(none.Found);
        Assert.Empty(none.Matches);
        Assert.Null(none.Role);
        Assert.Equal($"{unmapped} is in none of the groups the directory's group map on the Sign-in page gives a role, so a sign-in is refused.", none.Message);

        DirectoryCheck taken = await CheckAsync(local);
        Assert.Null(taken.Role);
        Assert.Equal($"{local} is a local account in DDT, so a sign-in with this name checks its DDT password and never asks the directory.", taken.Message);

        // A disabled account is refused before its groups count.
        string disabled = NewUser();
        using (SignedInClient browser = application.Browser())
        {
            Assert.Equal(LoginStatus.Succeeded, await browser.SignInAsync(disabled, DdtApplication.Password));
        }

        Guid id = await application.QueryAsync(database => database.Users.Where(u => u.UserName == disabled).Select(u => u.Id).SingleAsync(TestContext.Current.CancellationToken));
        (await (await application.AdministratorAsync()).PostAsync($"/api/users/{id}/disable")).EnsureSuccessStatusCode();
        Assert.Equal($"The DDT account {disabled} is disabled, so a sign-in is refused.", (await CheckAsync(disabled)).Message);
    }

    [Fact]
    public async Task ADirectoryThatDoesNotAnswerIsABadGatewayButTheMapStillShows()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        application.Ldap.Unavailable = true;

        try
        {
            using HttpResponseMessage search = await administrator.GetAsync($"{Directory}/groups?query=ddt");
            Assert.Equal(HttpStatusCode.BadGateway, search.StatusCode);
            Assert.StartsWith("The directory at dc.corp.example:636 could not be reached", await TestDatabase.TitleAsync(search), StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.BadGateway, (await administrator.PostAsync($"{Directory}/check", new DirectoryCheckRequest(NewUser()))).StatusCode);

            DirectoryView view = await ReadAsync<DirectoryView>(await administrator.GetAsync(Directory));
            Assert.Equal(4, view.GroupRoleMap.Count);
            Assert.All(view.GroupRoleMap, mapping => Assert.Null(mapping.Name));
        }
        finally
        {
            application.Ldap.Unavailable = false;
        }
    }

    [Fact]
    public async Task OnlyAdministratorsUseTheDirectoryTools()
    {
        using SignedInClient operatorClient = await application.SignedInBrowserAsync(await application.CreateUserAsync(DdtRoleNames.Operator), DdtApplication.Password);

        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync(Directory)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync($"{Directory}/groups?query=ddt")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsync($"{Directory}/check", new DirectoryCheckRequest(NewUser()))).StatusCode);

        using HttpResponseMessage blank = await (await application.AdministratorAsync()).PostAsync($"{Directory}/check", new DirectoryCheckRequest(" "));
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
    }

    [Fact]
    public async Task TheHighestMappedRoleWinsAndTheDirectoryStaysAuthoritative()
    {
        string userName = NewUser();
        application.Ldap.Groups[userName] = [ViewersGroup, AdministratorsGroup, OperatorsGroup];
        using SignedInClient browser = await application.SignedInBrowserAsync(userName, DdtApplication.Password);

        Assert.Equal([DdtRoleNames.Administrator], (await ReadAsync<CurrentUser>(await browser.GetAsync("/api/auth/me"))).Roles);

        application.Ldap.Groups[userName] = [ViewersGroup];
        using SignedInClient again = await application.SignedInBrowserAsync(userName, DdtApplication.Password);

        Assert.Equal([DdtRoleNames.Viewer], (await ReadAsync<CurrentUser>(await again.GetAsync("/api/auth/me"))).Roles);
    }

    // Distinguished names are compared as the directory compares them.
    [Fact]
    public async Task GroupsMatchTheMapWithoutRegardToCase()
    {
        string userName = NewUser();
        application.Ldap.Groups[userName] = [OperatorsGroup.ToUpperInvariant()];

        Assert.Equal(DdtRoleNames.Operator, (await CheckAsync(userName)).Role);
    }

    [Fact]
    public async Task AUserInNoneOfTheGroupsIsRefusedAndLosesItsRole()
    {
        string userName = NewUser();
        using (SignedInClient browser = application.Browser())
        {
            Assert.Equal(LoginStatus.Succeeded, await browser.SignInAsync(userName, DdtApplication.Password));
        }

        Guid id = await application.QueryAsync(database => database.Users.Where(u => u.UserName == userName).Select(u => u.Id).SingleAsync(TestContext.Current.CancellationToken));
        SignedInClient administrator = await application.AdministratorAsync();
        Assert.Equal(DdtRoleNames.Operator, (await administrator.UserAsync(id)).Role);

        application.Ldap.Groups[userName] = [UnmappedGroup];

        using (SignedInClient refused = application.Browser())
        {
            Assert.Equal(LoginStatus.NoRole, await refused.SignInAsync(userName, DdtApplication.Password));
            Assert.Equal(HttpStatusCode.Unauthorized, (await refused.GetAsync("/api/auth/me")).StatusCode);
        }

        UserView after = await administrator.UserAsync(id);
        Assert.Null(after.Role);
        Assert.Equal(RoleSource.DirectoryGroups, after.RoleFrom);

        // A user whose groups never gave a role gets no account and can't authorize a machine.
        string stranger = NewUser();
        application.Ldap.Groups[stranger] = [UnmappedGroup];
        using (SignedInClient refused = application.Browser())
        {
            Assert.Equal(LoginStatus.NoRole, await refused.SignInAsync(stranger, DdtApplication.Password));
        }

        Assert.False(await AccountExistsAsync(stranger));
        using RegisteredMachine machine = await application.RegisterMachineAsync();
        Assert.Equal(AgentSignInStatus.NotPermitted, (await machine.SignInAsync(stranger)).Status);

        // A wrong password is still only a wrong password.
        using SignedInClient guess = application.Browser();
        Assert.Null(await guess.SignInAsync(stranger, "not the password"));
    }

    [Fact]
    public async Task AdministratorsSeeTheRolesTheGroupsGiveAtEachSignIn()
    {
        await using LiveListener live = await LiveListener.StartAsync(application, await application.AdministratorAsync());
        ChannelReader<UserView> changes = live.Listen<UserView>(LiveEvents.UserChanged);
        string userName = NewUser();
        application.Ldap.Groups[userName] = [ViewersGroup];

        using (SignedInClient browser = application.Browser())
        {
            Assert.Equal(LoginStatus.Succeeded, await browser.SignInAsync(userName, DdtApplication.Password));
        }

        Assert.Equal(DdtRoleNames.Viewer, (await LiveListener.NextAsync(changes, user => user.UserName == userName)).Role);

        application.Ldap.Groups[userName] = [UnmappedGroup];

        using (SignedInClient browser = application.Browser())
        {
            Assert.Equal(LoginStatus.NoRole, await browser.SignInAsync(userName, DdtApplication.Password));
        }

        Assert.Equal(RoleSource.DirectoryGroups, (await LiveListener.NextAsync(changes, user => user.UserName == userName && user.Role is null)).RoleFrom);
    }

    [Fact]
    public async Task ARoleTheGroupsDecideIsNotChangedOnThePage()
    {
        string userName = NewUser();
        using (SignedInClient browser = application.Browser())
        {
            Assert.Equal(LoginStatus.Succeeded, await browser.SignInAsync(userName, DdtApplication.Password));
        }

        Guid id = await application.QueryAsync(database => database.Users.Where(u => u.UserName == userName).Select(u => u.Id).SingleAsync(TestContext.Current.CancellationToken));
        SignedInClient administrator = await application.AdministratorAsync();

        UserView view = await administrator.UserAsync(id);
        Assert.Equal(UserSource.Directory, view.Source);
        Assert.Equal(RoleSource.DirectoryGroups, view.RoleFrom);
        Assert.False(view.HasPassword);

        using HttpResponseMessage role = await administrator.PatchAsync($"/api/users/{id}", new UpdateUserRequest(null, null, DdtRoleNames.Administrator));
        Assert.Equal(HttpStatusCode.Conflict, role.StatusCode);
        Assert.Equal(
            $"The role of {userName} comes from its directory groups through the directory's group map, at each sign-in. Change its groups in the directory, or the map on the Sign-in page.",
            await TestDatabase.TitleAsync(role));

        using HttpResponseMessage name = await administrator.PatchAsync($"/api/users/{id}", new UpdateUserRequest("Someone Else", null, null));
        Assert.Equal(HttpStatusCode.Conflict, name.StatusCode);

        using HttpResponseMessage password = await administrator.PostAsync($"/api/users/{id}/reset-password");
        Assert.Equal($"{userName} signs in with its directory password. Reset it in the directory.", await TestDatabase.TitleAsync(password));
    }

    [Fact]
    public async Task TheToolsAreRefusedWhileTheDirectoryIsOff()
    {
        using DdtApplication plain = new();
        using SignedInClient administrator = await plain.SignInAsync(DdtRoleNames.Administrator);

        using HttpResponseMessage search = await administrator.GetAsync($"{Directory}/groups?query=ddt");
        Assert.Equal(HttpStatusCode.Conflict, search.StatusCode);
        Assert.Equal("Sign-in through a directory is off. Turn it on and set its connection on the Sign-in page first.", await TestDatabase.TitleAsync(search));
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.PostAsync($"{Directory}/check", new DirectoryCheckRequest("anyone"))).StatusCode);
        Assert.False((await ReadAsync<DirectoryView>(await administrator.GetAsync(Directory))).Enabled);
    }
}
