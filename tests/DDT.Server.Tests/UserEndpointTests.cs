// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using DDT.Contracts.Authentication;
using DDT.Contracts.Tokens;
using DDT.Contracts.Users;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DDT.Server.Tests.UserRequests;

namespace DDT.Server.Tests;

public sealed class UserEndpointTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static Task<T> ReadAsync<T>(HttpResponseMessage response) => RegisteredMachine.ReadAsync<T>(response);

    private static async Task<IEnumerable<string>> ProblemKeysAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestContext.Current.CancellationToken))!.Errors.Keys;
    }

    private static async Task<string?> ConflictAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        return await TestDatabase.TitleAsync(response);
    }

    private async Task<DdtUser> StoredAsync(Guid id) =>
        await application.QueryAsync(database => database.Users.AsNoTracking().SingleAsync(u => u.Id == id, TestContext.Current.CancellationToken));

    private async Task<Guid> IdOfAsync(string userName) =>
        await application.QueryAsync(database => database.Users.Where(u => u.UserName == userName).Select(u => u.Id).SingleAsync(TestContext.Current.CancellationToken));

    [Fact]
    public async Task ListsEveryAccountWithItsSourceItsRoleAndWhereTheRoleCameFrom()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        CreatedUser created = await administrator.CreatedUserAsync(DdtRoleNames.Operator);
        List<UserView> users = await ReadAsync<List<UserView>>(await administrator.GetAsync(UsersApi));

        UserView listed = Assert.Single(users, user => user.Id == created.User.Id);
        Assert.Equal(created.User, listed);
        Assert.Equal(UserSource.Local, listed.Source);
        Assert.Equal(DdtRoleNames.Operator, listed.Role);
        Assert.Equal(RoleSource.Manual, listed.RoleFrom);
        Assert.True(listed.HasPassword);
        Assert.True(listed.MustChangePassword);
        Assert.False(listed.Disabled);
        Assert.Null(listed.ExternalProvider);
        Assert.Null(listed.LastSignInUtc);

        UserView first = Assert.Single(users, user => user.UserName == "admin");
        Assert.Equal(DdtRoleNames.Administrator, first.Role);
        Assert.False(first.MustChangePassword);
    }

    // The highest role counts, and an account without one has nowhere it came from.
    [Fact]
    public async Task ShowsTheHighestRoleOfAnAccountWithSeveral()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Guid several = await IdOfAsync(await application.CreateUserAsync(DdtRoleNames.Viewer));
        Guid none = await IdOfAsync(await application.CreateUserAsync(DdtRoleNames.Viewer));

        using (IServiceScope scope = application.Services.CreateScope())
        {
            UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
            Assert.True((await users.AddToRoleAsync((await users.FindByIdAsync(several.ToString()))!, DdtRoleNames.Operator)).Succeeded);
            Assert.True((await users.RemoveFromRoleAsync((await users.FindByIdAsync(none.ToString()))!, DdtRoleNames.Viewer)).Succeeded);
        }

        Assert.Equal(DdtRoleNames.Operator, (await administrator.UserAsync(several)).Role);
        Assert.Null((await administrator.UserAsync(none)).Role);
        Assert.Null((await administrator.UserAsync(none)).RoleFrom);

        // Set here, the role is the only one the account has.
        UserView changed = await ReadAsync<UserView>(await administrator.PatchAsync($"{UsersApi}/{several}", new UpdateUserRequest(null, null, DdtRoleNames.Viewer)));
        Assert.Equal(DdtRoleNames.Viewer, changed.Role);
        Assert.Equal(
            [DdtRoleNames.Viewer],
            await application.QueryAsync(database => database.UserRoles
                .Where(ur => ur.UserId == several)
                .Join(database.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
                .ToListAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task ANewAccountSignsInWithItsOneTimePasswordAndReachesNothingUntilItChangesIt()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        CreatedUser created = await administrator.CreatedUserAsync(DdtRoleNames.Operator);
        using SignedInClient browser = await application.SignedInBrowserAsync(created.User.UserName, created.Password);

        CurrentUser me = await ReadAsync<CurrentUser>(await browser.GetAsync("/api/auth/me"));
        Assert.True(me.MustChangePassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync("/api/machines")).StatusCode);
        await Assert.ThrowsAnyAsync<Exception>(async () => await LiveListener.StartAsync(application, browser));
        // Nor can it make a token to get past that.
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsync("/api/tokens", new CreateApiTokenRequest("script", DdtRoleNames.Viewer))).StatusCode);

        const string own = "A password only I know 7";
        (await browser.PostAsync("/api/auth/password", new ChangePasswordRequest(created.Password, own))).EnsureSuccessStatusCode();

        Assert.False((await ReadAsync<CurrentUser>(await browser.GetAsync("/api/auth/me"))).MustChangePassword);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/machines")).StatusCode);
        Assert.False((await administrator.UserAsync(created.User.Id)).MustChangePassword);
    }

    // At a machine too, the password an administrator was shown authorizes nothing.
    [Fact]
    public async Task ANewAccountAuthorizesNoMachineBeforeItChangesItsPassword()
    {
        CreatedUser created = await (await application.AdministratorAsync()).CreatedUserAsync(DdtRoleNames.Operator);
        using RegisteredMachine machine = await application.RegisterMachineAsync();

        Assert.Equal(DDT.Contracts.Agents.AgentSignInStatus.NotPermitted, (await machine.SignInAsync(created.User.UserName, created.Password)).Status);
    }

    [Fact]
    public async Task CreationNamesEachProblemByItsField()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        Assert.Equal(
            ["userName", "displayName", "email", "role"],
            await ProblemKeysAsync(await administrator.PostAsync(UsersApi, new CreateUserRequest(" ", "", "Jane <jane@corp.example>", "Viewers"))));

        CreatedUser created = await administrator.CreatedUserAsync(DdtRoleNames.Viewer);

        Assert.Equal(["userName"], await ProblemKeysAsync(await administrator.PostAsync(UsersApi, new CreateUserRequest(created.User.UserName, "Again", null, DdtRoleNames.Viewer))));
        Assert.Equal(["userName"], await ProblemKeysAsync(await administrator.PostAsync(UsersApi, new CreateUserRequest("o'brien", "O'Brien", null, DdtRoleNames.Viewer))));
        Assert.DoesNotContain(await ReadAsync<List<UserView>>(await administrator.GetAsync(UsersApi)), user => user.UserName == "o'brien");
    }

    [Fact]
    public async Task ChangesAreAuditedFieldByField()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        CreatedUser created = await administrator.CreatedUserAsync(DdtRoleNames.Viewer);
        Guid id = created.User.Id;

        UserView changed = await ReadAsync<UserView>(await administrator.PatchAsync($"{UsersApi}/{id}", new UpdateUserRequest("Jane Doe", "jane@corp.example", "operator")));

        Assert.Equal("Jane Doe", changed.DisplayName);
        Assert.Equal("jane@corp.example", changed.Email);
        Assert.Equal(DdtRoleNames.Operator, changed.Role);

        // Left out, a field stays; empty, it is cleared. The same values again change nothing and write no row.
        UserView cleared = await ReadAsync<UserView>(await administrator.PatchAsync($"{UsersApi}/{id}", new UpdateUserRequest(null, "", null)));
        Assert.Equal("Jane Doe", cleared.DisplayName);
        Assert.Null(cleared.Email);
        _ = await ReadAsync<UserView>(await administrator.PatchAsync($"{UsersApi}/{id}", new UpdateUserRequest("Jane Doe", null, DdtRoleNames.Operator)));

        List<AuditEvent> audit = await application.UserAuditAsync(id);
        Assert.Equal([AuditActions.UserCreated, AuditActions.UserChanged, AuditActions.UserChanged], audit.Select(e => e.Action));
        Assert.Equal($"Local account {created.User.UserName} with the role Viewer.", audit[0].Detail);
        Assert.Equal("displayName: 'Test User' to 'Jane Doe'; email: '' to 'jane@corp.example'; role: 'Viewer' to 'Operator'", audit[1].Detail);
        Assert.Equal("email: 'jane@corp.example' to ''", audit[2].Detail);
        Assert.All(audit, e => Assert.StartsWith("administrator-", e.ActorName, StringComparison.Ordinal));

        Assert.Equal(["email"], await ProblemKeysAsync(await administrator.PatchAsync($"{UsersApi}/{id}", new UpdateUserRequest(null, "not an address", null))));
        Assert.Equal(["role"], await ProblemKeysAsync(await administrator.PatchAsync($"{UsersApi}/{id}", new UpdateUserRequest(null, null, "Owner"))));
    }

    [Fact]
    public async Task AnAdministratorCannotDisableDeleteDemoteOrResetThemselves()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Guid self = (await ReadAsync<CurrentUser>(await administrator.GetAsync("/api/auth/me"))).Id;

        Assert.Equal(
            "You cannot disable your own account. Another administrator can.",
            await ConflictAsync(await administrator.PostAsync($"{UsersApi}/{self}/disable")));
        Assert.Equal(
            "You cannot delete your own account. Another administrator can.",
            await ConflictAsync(await administrator.DeleteAsync($"{UsersApi}/{self}")));
        Assert.Equal(
            "You cannot take the Administrator role from your own account. Another administrator can.",
            await ConflictAsync(await administrator.PatchAsync($"{UsersApi}/{self}", new UpdateUserRequest(null, null, DdtRoleNames.Operator))));
        Assert.Equal("Change your own password on the Account page.", await ConflictAsync(await administrator.PostAsync($"{UsersApi}/{self}/reset-password")));
        Assert.Equal("Turn off your own second factor on the Account page.", await ConflictAsync(await administrator.PostAsync($"{UsersApi}/{self}/reset-two-factor")));

        // Its name is still its own to change.
        Assert.Equal("Me", (await ReadAsync<UserView>(await administrator.PatchAsync($"{UsersApi}/{self}", new UpdateUserRequest("Me", null, DdtRoleNames.Administrator)))).DisplayName);
        Assert.Empty(await application.QueryAsync(database => database.AuditEvents
            .Where(e => e.SubjectId == self.ToString() && e.Action != AuditActions.UserChanged)
            .ToListAsync(TestContext.Current.CancellationToken)));
    }

    // Refusing an administrator itself is not enough: a cookie keeps a role for up to a minute after it was taken away.
    [Fact]
    public async Task TheLastEnabledAdministratorKeepsTheRole()
    {
        using DdtApplication own = new();
        using SignedInClient first = await own.SignInAsync(DdtRoleNames.Administrator);
        CreatedUser second = await first.CreatedUserAsync(DdtRoleNames.Administrator);
        using SignedInClient secondBrowser = await own.SignedInBrowserAsync(second.User.UserName, second.Password);
        (await secondBrowser.PostAsync("/api/auth/password", new ChangePasswordRequest(second.Password, DdtApplication.Password))).EnsureSuccessStatusCode();
        Guid firstId = (await ReadAsync<CurrentUser>(await first.GetAsync("/api/auth/me"))).Id;
        Guid bootstrap = await own.QueryAsync(database => database.Users.Where(u => u.UserName == "admin").Select(u => u.Id).SingleAsync(TestContext.Current.CancellationToken));

        (await first.PostAsync($"{UsersApi}/{bootstrap}/disable")).EnsureSuccessStatusCode();
        (await first.PatchAsync($"{UsersApi}/{second.User.Id}", new UpdateUserRequest(null, null, DdtRoleNames.Viewer))).EnsureSuccessStatusCode();

        string last = $"{(await StoredIn(own, firstId)).UserName} is the last enabled administrator. Make another account an administrator first.";
        Assert.Equal(last, await ConflictAsync(await secondBrowser.PostAsync($"{UsersApi}/{firstId}/disable")));
        Assert.Equal(last, await ConflictAsync(await secondBrowser.DeleteAsync($"{UsersApi}/{firstId}")));
        Assert.Equal(last, await ConflictAsync(await secondBrowser.PatchAsync($"{UsersApi}/{firstId}", new UpdateUserRequest(null, null, DdtRoleNames.Viewer))));

        // A disabled administrator is no administrator anyone could sign in as, so it may lose the role.
        (await first.PatchAsync($"{UsersApi}/{bootstrap}", new UpdateUserRequest(null, null, DdtRoleNames.Viewer))).EnsureSuccessStatusCode();
        Assert.False((await StoredIn(own, firstId)).IsDisabled);

        static Task<DdtUser> StoredIn(DdtApplication host, Guid id) =>
            host.QueryAsync(database => database.Users.AsNoTracking().SingleAsync(u => u.Id == id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisablingEndsTheSessionAndClosesTheLiveConnection()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        CreatedUser created = await administrator.CreatedUserAsync(DdtRoleNames.Viewer);
        using SignedInClient browser = await application.SignedInBrowserAsync(created.User.UserName, created.Password);
        (await browser.PostAsync("/api/auth/password", new ChangePasswordRequest(created.Password, DdtApplication.Password))).EnsureSuccessStatusCode();
        await using LiveListener live = await LiveListener.StartAsync(application, browser);
        string stamp = (await StoredAsync(created.User.Id)).SecurityStamp!;

        UserView disabled = await ReadAsync<UserView>(await administrator.PostAsync($"{UsersApi}/{created.User.Id}/disable"));

        Assert.True(disabled.Disabled);
        Assert.NotEqual(stamp, (await StoredAsync(created.User.Id)).SecurityStamp);
        await live.Closed.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/auth/me")).StatusCode);
        using SignedInClient refused = application.Browser();
        Assert.Null(await refused.SignInAsync(created.User.UserName, DdtApplication.Password));

        // Disabling twice changes nothing more; enabling lets the account sign in again.
        Assert.True((await ReadAsync<UserView>(await administrator.PostAsync($"{UsersApi}/{created.User.Id}/disable"))).Disabled);
        Assert.False((await ReadAsync<UserView>(await administrator.PostAsync($"{UsersApi}/{created.User.Id}/enable"))).Disabled);
        using SignedInClient welcomed = application.Browser();
        Assert.Equal(LoginStatus.Succeeded, await welcomed.SignInAsync(created.User.UserName, DdtApplication.Password));
        Assert.Equal(
            [AuditActions.UserCreated, AuditActions.UserDisabled, AuditActions.UserEnabled],
            (await application.UserAuditAsync(created.User.Id)).Select(e => e.Action));
    }

    // Groups do not follow a role taken away while a connection is open, so the connection is closed and the page
    // connects again with what the account holds now.
    [Fact]
    public async Task ADemotedAdministratorStopsReceivingWhatOnlyAdministratorsSee()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        CreatedUser created = await administrator.CreatedUserAsync(DdtRoleNames.Administrator);
        using SignedInClient browser = await application.SignedInBrowserAsync(created.User.UserName, created.Password);
        (await browser.PostAsync("/api/auth/password", new ChangePasswordRequest(created.Password, DdtApplication.Password))).EnsureSuccessStatusCode();
        await using LiveListener live = await LiveListener.StartAsync(application, browser);

        (await administrator.PatchAsync($"{UsersApi}/{created.User.Id}", new UpdateUserRequest(null, null, DdtRoleNames.Viewer))).EnsureSuccessStatusCode();
        await live.Closed.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        // The cookie still says Administrator, but the hub reads the account.
        await using LiveListener again = await LiveListener.StartAsync(application, browser);
        ChannelReader<UserView> changes = again.Listen<UserView>(LiveEvents.UserChanged);
        await administrator.CreatedUserAsync(DdtRoleNames.Viewer);
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        Assert.False(changes.TryRead(out _));
    }

    [Fact]
    public async Task AdministratorsSeeEveryChangeAndEverySignInLive()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using SignedInClient viewer = await application.SignedInBrowserAsync(await application.CreateUserAsync(DdtRoleNames.Viewer), DdtApplication.Password);
        await using LiveListener live = await LiveListener.StartAsync(application, administrator);
        await using LiveListener bystander = await LiveListener.StartAsync(application, viewer);
        ChannelReader<UserView> changes = live.Listen<UserView>(LiveEvents.UserChanged);
        ChannelReader<UsersRemovedEvent> removals = live.Listen<UsersRemovedEvent>(LiveEvents.UsersRemoved);
        ChannelReader<UserView> overheard = bystander.Listen<UserView>(LiveEvents.UserChanged);

        CreatedUser created = await administrator.CreatedUserAsync(DdtRoleNames.Viewer);
        Assert.Equal(created.User, await LiveListener.NextAsync(changes, user => user.Id == created.User.Id));

        UserView renamed = await ReadAsync<UserView>(await administrator.PatchAsync($"{UsersApi}/{created.User.Id}", new UpdateUserRequest("Renamed", null, null)));
        Assert.Equal(renamed, await LiveListener.NextAsync(changes, user => user.Id == created.User.Id));

        using SignedInClient browser = await application.SignedInBrowserAsync(created.User.UserName, created.Password);
        UserView signedIn = await LiveListener.NextAsync(changes, user => user.Id == created.User.Id);
        Assert.NotNull(signedIn.LastSignInUtc);
        Assert.Equal(signedIn.LastSignInUtc, (await administrator.UserAsync(created.User.Id)).LastSignInUtc);

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{UsersApi}/{created.User.Id}")).StatusCode);
        Assert.Equal([created.User.Id], (await LiveListener.NextAsync(removals)).UserIds);
        Assert.DoesNotContain(await ReadAsync<List<UserView>>(await administrator.GetAsync(UsersApi)), user => user.Id == created.User.Id);
        Assert.Equal(AuditActions.UserDeleted, (await application.UserAuditAsync(created.User.Id))[^1].Action);

        Assert.False(overheard.TryRead(out _));
    }

    // What an account does to itself reaches the Users page too.
    [Fact]
    public async Task AdministratorsSeeASecondFactorAndALockoutAsTheyHappen()
    {
        string userName = await application.CreateUserAsync(DdtRoleNames.Viewer);
        Guid id = await IdOfAsync(userName);
        await using LiveListener live = await LiveListener.StartAsync(application, await application.AdministratorAsync());
        ChannelReader<UserView> changes = live.Listen<UserView>(LiveEvents.UserChanged);
        using SignedInClient browser = await application.SignedInBrowserAsync(userName, DdtApplication.Password);

        TwoFactorEnrollment enrollment = await ReadAsync<TwoFactorEnrollment>(await browser.PostAsync("/api/auth/2fa/enroll"));
        string code = Totp.Code(enrollment.SharedKey.Replace(" ", string.Empty, StringComparison.Ordinal), DateTimeOffset.UtcNow);
        (await browser.PostAsync("/api/auth/2fa/enable", new TwoFactorVerifyRequest(code))).EnsureSuccessStatusCode();

        Assert.True((await LiveListener.NextAsync(changes, user => user.Id == id && user.TwoFactorEnabled)).TwoFactorEnabled);

        using SignedInClient guesser = application.Browser();
        LoginStatus? status = null;

        for (int attempt = 0; attempt < 5 && status != LoginStatus.LockedOut; attempt++)
        {
            status = await guesser.SignInAsync(userName, "not the password");
        }

        Assert.Equal(LoginStatus.LockedOut, status);
        Assert.NotNull((await LiveListener.NextAsync(changes, user => user.Id == id && user.LockedOutUntil is not null)).LockedOutUntil);
    }

    [Fact]
    public async Task OnlyAdministratorsUseTheUsersApi()
    {
        using SignedInClient operatorClient = await application.SignedInBrowserAsync(await application.CreateUserAsync(DdtRoleNames.Operator), DdtApplication.Password);
        Guid anyone = await IdOfAsync("admin");

        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync(UsersApi)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsync(UsersApi, Local(DdtRoleNames.Administrator))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsync($"{UsersApi}/{anyone}/disable")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PatchAsync($"{UsersApi}/{anyone}", new UpdateUserRequest(null, null, DdtRoleNames.Viewer))).StatusCode);
        using HttpClient anonymous = application.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(new Uri(UsersApi, UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task AnUnknownAccountIsNotFound()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string unknown = $"{UsersApi}/{Guid.NewGuid()}";

        Assert.Equal(HttpStatusCode.NotFound, (await administrator.PatchAsync(unknown, new UpdateUserRequest("x", null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.PostAsync($"{unknown}/disable")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.PostAsync($"{unknown}/enable")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.PostAsync($"{unknown}/reset-password")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.PostAsync($"{unknown}/reset-two-factor")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.DeleteAsync(unknown)).StatusCode);
    }

    [Fact]
    public async Task APasswordResetEndsALockoutAndMustBeChangedAgain()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        CreatedUser created = await administrator.CreatedUserAsync(DdtRoleNames.Viewer);
        using SignedInClient browser = await application.SignedInBrowserAsync(created.User.UserName, created.Password);
        (await browser.PostAsync("/api/auth/password", new ChangePasswordRequest(created.Password, DdtApplication.Password))).EnsureSuccessStatusCode();

        using (IServiceScope scope = application.Services.CreateScope())
        {
            UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
            Assert.True((await users.SetLockoutEndDateAsync((await users.FindByIdAsync(created.User.Id.ToString()))!, DateTimeOffset.UtcNow.AddHours(1))).Succeeded);
        }

        Assert.NotNull((await administrator.UserAsync(created.User.Id)).LockedOutUntil);

        OneTimePassword reset = await ReadAsync<OneTimePassword>(await administrator.PostAsync($"{UsersApi}/{created.User.Id}/reset-password"));

        Assert.NotEqual(created.Password, reset.Password);
        UserView after = await administrator.UserAsync(created.User.Id);
        Assert.Null(after.LockedOutUntil);
        Assert.True(after.MustChangePassword);
        using SignedInClient old = application.Browser();
        Assert.Null(await old.SignInAsync(created.User.UserName, DdtApplication.Password));
        using SignedInClient again = await application.SignedInBrowserAsync(created.User.UserName, reset.Password);
        Assert.True((await ReadAsync<CurrentUser>(await again.GetAsync("/api/auth/me"))).MustChangePassword);
        Assert.Equal(AuditActions.UserPasswordReset, (await application.UserAuditAsync(created.User.Id))[^1].Action);
        Assert.DoesNotContain(reset.Password, (await application.UserAuditAsync(created.User.Id))[^1].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASecondFactorResetTurnsItOffWithANewKey()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Guid id = await IdOfAsync(await application.CreateUserAsync(DdtRoleNames.Viewer));
        string oldKey;

        using (IServiceScope scope = application.Services.CreateScope())
        {
            UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
            DdtUser user = (await users.FindByIdAsync(id.ToString()))!;
            await users.ResetAuthenticatorKeyAsync(user);
            await users.SetTwoFactorEnabledAsync(user, true);
            oldKey = (await users.GetAuthenticatorKeyAsync(user))!;
        }

        Assert.True((await administrator.UserAsync(id)).TwoFactorEnabled);

        UserView reset = await ReadAsync<UserView>(await administrator.PostAsync($"{UsersApi}/{id}/reset-two-factor"));

        Assert.False(reset.TwoFactorEnabled);
        using (IServiceScope scope = application.Services.CreateScope())
        {
            UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
            Assert.NotEqual(oldKey, await users.GetAuthenticatorKeyAsync((await users.FindByIdAsync(id.ToString()))!));
        }

        Assert.Equal(AuditActions.UserTwoFactorReset, (await application.UserAuditAsync(id))[^1].Action);
    }

    // Single sign-on stored its accounts as directory accounts before, and a password typed for one went to the directory.
    [Fact]
    public async Task EarlierSingleSignOnAccountsAreToldApartFromDirectoryAccounts()
    {
        Guid legacy = Guid.NewGuid();
        Guid directory = Guid.NewGuid();

        using (IServiceScope scope = application.Services.CreateScope())
        {
            UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
            Assert.True((await users.CreateAsync(new DdtUser { Id = legacy, UserName = $"sso-{legacy:N}", Source = AccountSource.Directory })).Succeeded);
            Assert.True((await users.CreateAsync(new DdtUser { Id = directory, UserName = $"dir-{directory:N}", Source = AccountSource.Directory, DirectoryObjectId = $"id-{directory:N}" })).Succeeded);

            await ActivatorUtilities.CreateInstance<IdentityBootstrap>(scope.ServiceProvider).StartAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(AccountSource.External, (await StoredAsync(legacy)).Source);
        Assert.Equal(AccountSource.Directory, (await StoredAsync(directory)).Source);
        Assert.Equal(UserSource.External, (await (await application.AdministratorAsync()).UserAsync(legacy)).Source);

        // No password is checked for it, so guessing one neither signs in nor locks the account out of its single sign-on.
        for (int attempt = 0; attempt < 6; attempt++)
        {
            using SignedInClient guess = application.Browser();
            Assert.Null(await guess.SignInAsync($"sso-{legacy:N}", DdtApplication.Password));
        }

        Assert.Null((await StoredAsync(legacy)).LockoutEnd);
        Assert.Equal(0, (await StoredAsync(legacy)).AccessFailedCount);
    }
}
