// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Threading.Channels;
using DDT.Contracts.Audit;
using DDT.Contracts.Authentication;
using DDT.Contracts.Machines;
using DDT.Contracts.Tokens;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Tokens;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

public sealed class ApiTokenTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private const string Tokens = "/api/tokens";

    private static async Task<IDictionary<string, string[]>> ProblemsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestContext.Current.CancellationToken))!.Errors;
    }

    private async Task<Guid> PendingMachineAsync()
    {
        using RegisteredMachine machine = await application.RegisterMachineAsync();

        return machine.Id;
    }

    private async Task ChangeUserAsync(string userName, Func<UserManager<DdtUser>, DdtUser, Task> change)
    {
        using IServiceScope scope = application.Services.CreateScope();
        UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
        DdtUser user = (await users.FindByNameAsync(userName))!;

        await change(users, user);
    }

    private async Task<HubConnection> TokenHubAsync(string secret, string liveEvent, Channel<ApiTokenView> received)
    {
        HubConnection connection = new HubConnectionBuilder()
            .WithUrl(new Uri(application.Server.BaseAddress, "hubs/live"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => application.Server.CreateHandler();
                options.Headers["Authorization"] = $"Bearer {secret}";
            })
            .AddJsonProtocol(json => json.PayloadSerializerOptions = TestJson.Options)
            .Build();

        connection.On<ApiTokenView>(liveEvent, token => received.Writer.TryWrite(token));
        await connection.StartAsync(TestContext.Current.CancellationToken);

        return connection;
    }

    [Fact]
    public async Task ShowsTheSecretOnceAndKeepsOnlyItsHash()
    {
        using SignedInClient operatorClient = await application.SignInAsync(DdtRoleNames.Operator);
        CurrentUser me = await RegisteredMachine.ReadAsync<CurrentUser>(await operatorClient.GetAsync("/api/auth/me"));

        HttpResponseMessage response = await operatorClient.PostAsync(Tokens, new CreateApiTokenRequest(" build-server ", "operator", 30));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        CreatedApiToken created = await RegisteredMachine.ReadAsync<CreatedApiToken>(response);

        Assert.StartsWith(ApiTokenSecrets.Prefix, created.Secret, StringComparison.Ordinal);
        Assert.Equal(47, created.Secret.Length);
        Assert.Equal("build-server", created.Token.Name);
        Assert.Equal(DdtRoleNames.Operator, created.Token.Role);
        Assert.Equal(me.Id, created.Token.UserId);
        Assert.Equal(me.UserName, created.Token.UserName);
        Assert.Equal(created.Secret[^4..], created.Token.Hint);
        Assert.Equal(created.Token.CreatedUtc.AddDays(30), created.Token.ExpiresUtc);
        Assert.Null(created.Token.LastUsedUtc);
        Assert.Null(created.Token.RevokedUtc);

        IReadOnlyList<ApiTokenView> listed = await RegisteredMachine.ReadAsync<IReadOnlyList<ApiTokenView>>(await operatorClient.GetAsync(Tokens));
        Assert.Equal(created.Token, Assert.Single(listed));
        Assert.DoesNotContain(created.Secret, await (await operatorClient.GetAsync(Tokens)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);

        ApiToken stored = await application.TokenAsync(created.Token.Id);
        Assert.Equal(ApiTokenSecrets.Hash(created.Secret), stored.SecretHash);

        // It isn't in the database files either, whatever column or page it might have ended up in.
        foreach (string file in Directory.EnumerateFiles(application.StorePath, "ddt-dev.db*"))
        {
            await using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using MemoryStream copy = new();
            await stream.CopyToAsync(copy, TestContext.Current.CancellationToken);

            Assert.DoesNotContain(created.Secret[ApiTokenSecrets.Prefix.Length..], Encoding.Latin1.GetString(copy.ToArray()), StringComparison.Ordinal);
        }

        AuditEvent audit = await application.QueryAsync(database => database.AuditEvents.AsNoTracking().SingleAsync(
            e => e.SubjectId == created.Token.Id.ToString("D"),
            TestContext.Current.CancellationToken));
        Assert.Equal(AuditActions.TokenCreated, audit.Action);
        Assert.Equal(me.Id, audit.ActorUserId);
        Assert.StartsWith($"build-server, Operator, ending in {created.Token.Hint}, expires ", audit.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesATokenItsUserCouldNotHave()
    {
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);

        Assert.Contains("role", (await ProblemsAsync(await viewer.PostAsync(Tokens, new CreateApiTokenRequest("ci", DdtRoleNames.Operator)))).Keys);
        Assert.Contains("role", (await ProblemsAsync(await viewer.PostAsync(Tokens, new CreateApiTokenRequest("ci", "Owner")))).Keys);
        Assert.Contains("name", (await ProblemsAsync(await viewer.PostAsync(Tokens, new CreateApiTokenRequest(" ", DdtRoleNames.Viewer)))).Keys);
        Assert.Contains("name", (await ProblemsAsync(await viewer.PostAsync(Tokens, new CreateApiTokenRequest(new string('x', 65), DdtRoleNames.Viewer)))).Keys);
        Assert.Contains("expiresInDays", (await ProblemsAsync(await viewer.PostAsync(Tokens, new CreateApiTokenRequest("ci", DdtRoleNames.Viewer, 0)))).Keys);
        Assert.Contains("expiresInDays", (await ProblemsAsync(await viewer.PostAsync(Tokens, new CreateApiTokenRequest("ci", DdtRoleNames.Viewer, 366)))).Keys);

        // Without a lifetime, a token lasts 90 days. Its name stays taken until it's revoked.
        CreatedApiToken first = await RegisteredMachine.ReadAsync<CreatedApiToken>(await viewer.PostAsync(Tokens, new CreateApiTokenRequest("ci", DdtRoleNames.Viewer)));
        Assert.Equal(first.Token.CreatedUtc.AddDays(ApiTokenLimits.DefaultDays), first.Token.ExpiresUtc);
        Assert.Contains("name", (await ProblemsAsync(await viewer.PostAsync(Tokens, new CreateApiTokenRequest("ci", DdtRoleNames.Viewer)))).Keys);

        Assert.Equal(HttpStatusCode.NoContent, (await viewer.DeleteAsync($"{Tokens}/{first.Token.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await viewer.PostAsync(Tokens, new CreateApiTokenRequest("ci", DdtRoleNames.Viewer, 365))).StatusCode);
    }

    // A script sends neither a cookie nor an antiforgery token, and its POST still goes through.
    [Fact]
    public async Task AViewerTokenCannotApproveAMachineAndAnOperatorTokenCan()
    {
        string userName = await application.CreateUserAsync(DdtRoleNames.Operator);
        (_, string viewerSecret) = await application.SeedTokenAsync(userName, DdtRoleNames.Viewer);
        (_, string operatorSecret) = await application.SeedTokenAsync(userName, DdtRoleNames.Operator);
        using HttpClient viewer = application.TokenClient(viewerSecret);
        using HttpClient operatorClient = application.TokenClient(operatorSecret);
        Guid machine = await PendingMachineAsync();

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetPathAsync("/api/machines")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostJsonAsync($"/api/machines/{machine}/approve")).StatusCode);

        HttpResponseMessage approved = await operatorClient.PostJsonAsync($"/api/machines/{machine}/approve");
        Assert.Equal(MachineState.Approved, (await RegisteredMachine.ReadAsync<MachineSummary>(approved)).State);
    }

    [Fact]
    public async Task ATokenDoesNoMoreThanItsUserMayNow()
    {
        string userName = await application.CreateUserAsync(DdtRoleNames.Operator);
        (_, string secret) = await application.SeedTokenAsync(userName, DdtRoleNames.Administrator);
        using HttpClient client = application.TokenClient(secret);

        // The token says Administrator, but its user is an operator, so it acts as an operator.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetPathAsync("/api/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostJsonAsync($"/api/machines/{await PendingMachineAsync()}/approve")).StatusCode);

        await ChangeUserAsync(userName, async (users, user) =>
        {
            await users.RemoveFromRoleAsync(user, DdtRoleNames.Operator);
            await users.AddToRoleAsync(user, DdtRoleNames.Viewer);
        });

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostJsonAsync($"/api/machines/{await PendingMachineAsync()}/approve")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetPathAsync("/api/machines")).StatusCode);

        await ChangeUserAsync(userName, (users, user) => users.RemoveFromRoleAsync(user, DdtRoleNames.Viewer));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetPathAsync("/api/machines")).StatusCode);
    }

    [Fact]
    public async Task AnExpiredTokenStopsWorking()
    {
        string userName = await application.CreateUserAsync(DdtRoleNames.Viewer);
        (Guid id, string secret) = await application.SeedTokenAsync(userName, DdtRoleNames.Viewer);
        using HttpClient client = application.TokenClient(secret);
        Assert.Equal(HttpStatusCode.OK, (await client.GetPathAsync("/api/machines")).StatusCode);

        await application.ChangeTokenAsync(id, token => token.ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(-1));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetPathAsync("/api/machines")).StatusCode);
    }

    [Fact]
    public async Task ADisabledOrLockedOutUsersTokensStopWorking()
    {
        string userName = await application.CreateUserAsync(DdtRoleNames.Viewer);
        (_, string secret) = await application.SeedTokenAsync(userName, DdtRoleNames.Viewer);
        using HttpClient client = application.TokenClient(secret);

        await ChangeUserAsync(userName, (users, user) =>
        {
            user.IsDisabled = true;

            return users.UpdateAsync(user);
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetPathAsync("/api/machines")).StatusCode);

        await ChangeUserAsync(userName, async (users, user) =>
        {
            user.IsDisabled = false;
            await users.UpdateAsync(user);
            await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(10));
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetPathAsync("/api/machines")).StatusCode);

        await ChangeUserAsync(userName, (users, user) => users.SetLockoutEndDateAsync(user, null));
        Assert.Equal(HttpStatusCode.OK, (await client.GetPathAsync("/api/machines")).StatusCode);
    }

    [Fact]
    public async Task TheOwnerOrAnAdministratorRevokesATokenAndItsRowStays()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string ownerName = await application.CreateUserAsync(DdtRoleNames.Operator);
        string otherName = await application.CreateUserAsync(DdtRoleNames.Operator);
        (Guid own, string ownSecret) = await application.SeedTokenAsync(ownerName, DdtRoleNames.Viewer);
        (Guid second, string secondSecret) = await application.SeedTokenAsync(ownerName, DdtRoleNames.Viewer);
        (_, string otherSecret) = await application.SeedTokenAsync(otherName, DdtRoleNames.Operator);
        using HttpClient owner = application.TokenClient(ownSecret);
        using HttpClient other = application.TokenClient(otherSecret);
        using HttpClient revoked = application.TokenClient(secondSecret);

        Assert.Equal(HttpStatusCode.Forbidden, (await other.DeletePathAsync($"{Tokens}/{own}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeletePathAsync($"{Tokens}/{Guid.NewGuid()}")).StatusCode);

        // The owner's token revokes the owner's other token, and then the administrator revokes this one.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeletePathAsync($"{Tokens}/{second}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await revoked.GetPathAsync("/api/machines")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{Tokens}/{own}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetPathAsync("/api/machines")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{Tokens}/{own}")).StatusCode);

        IReadOnlyList<ApiTokenView> all = await RegisteredMachine.ReadAsync<IReadOnlyList<ApiTokenView>>(await administrator.GetAsync($"{Tokens}/all"));
        ApiTokenView byAdministrator = Assert.Single(all, t => t.Id == own);
        Assert.NotNull(byAdministrator.RevokedUtc);
        Assert.StartsWith("administrator-", byAdministrator.RevokedByName, StringComparison.Ordinal);
        Assert.Equal(ownerName, byAdministrator.UserName);
        Assert.Equal($"{ownerName} (token {(await application.TokenAsync(own)).Name})", Assert.Single(all, t => t.Id == second).RevokedByName);

        List<AuditEvent> audits = await application.QueryAsync(database => database.AuditEvents
            .AsNoTracking()
            .Where(e => e.Action == AuditActions.TokenRevoked && (e.SubjectId == own.ToString("D") || e.SubjectId == second.ToString("D")))
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, audits.Count);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetPathAsync($"{Tokens}/all")).StatusCode);
    }

    [Fact]
    public async Task EndpointsThatChangeTheAccountRefuseATokenButItKnowsWhoItIs()
    {
        string userName = await application.CreateUserAsync(DdtRoleNames.Administrator);
        (_, string secret) = await application.SeedTokenAsync(userName, DdtRoleNames.Administrator);
        using HttpClient client = application.TokenClient(secret);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostJsonAsync("/api/auth/password", new ChangePasswordRequest(DdtApplication.Password, "Another password 42"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostJsonAsync("/api/auth/2fa/enroll")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostJsonAsync("/api/auth/2fa/disable")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostJsonAsync("/api/auth/external/link")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostJsonAsync(Tokens, new CreateApiTokenRequest("successor", DdtRoleNames.Administrator))).StatusCode);

        CurrentUser me = await RegisteredMachine.ReadAsync<CurrentUser>(await client.GetPathAsync("/api/auth/me"));
        Assert.Equal(userName, me.UserName);
    }

    // An account that must replace a password an administrator was shown can't reach anything with its session.
    // So it can't reach anything with a token it made earlier either.
    [Fact]
    public async Task ATokenReachesNothingWhileItsAccountHasToChangeItsPassword()
    {
        string userName = await application.CreateUserAsync(DdtRoleNames.Operator);
        (_, string secret) = await application.SeedTokenAsync(userName, DdtRoleNames.Viewer);
        await ChangeUserAsync(userName, (users, user) => users.AddClaimAsync(user, new Claim(DdtClaimTypes.MustChangePassword, "true")));

        using HttpClient client = application.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/api/machines", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);
    }

    // A request that carries a token is the token's, whatever session cookie the client also holds.
    [Fact]
    public async Task ASessionCookieThatRidesAlongGrantsNothing()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string userName = await application.CreateUserAsync(DdtRoleNames.Viewer);
        (_, string secret) = await application.SeedTokenAsync(userName, DdtRoleNames.Viewer);
        Guid machine = await PendingMachineAsync();

        using HttpRequestMessage request = new(HttpMethod.Post, new Uri($"/api/machines/{machine}/approve", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        Assert.Equal(HttpStatusCode.Forbidden, (await administrator.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);

        using HttpRequestMessage garbage = new(HttpMethod.Get, new Uri("/api/machines", UriKind.Relative));
        garbage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");

        Assert.Equal(HttpStatusCode.Unauthorized, (await administrator.Http.SendAsync(garbage, TestContext.Current.CancellationToken)).StatusCode);
    }

    // A user claim with the token's claim type ends up in the session cookie.
    // It must not make the session count as a token, because tokens skip the CSRF filters.
    [Fact]
    public async Task AClaimNamedLikeATokensDoesNotMakeASessionOne()
    {
        string userName = await application.CreateUserAsync(DdtRoleNames.Operator);
        await ChangeUserAsync(userName, (users, user) => users.AddClaimAsync(user, new Claim(DdtClaimTypes.ApiTokenId, Guid.NewGuid().ToString("D"))));

        CookieContainer cookies = new();
        using SignedInClient session = new(application.CreateDefaultClient(new CookieContainerHandler(cookies)), cookies);
        (await session.PostAsync("/api/auth/login", new LoginRequest(userName, DdtApplication.Password, null, null))).EnsureSuccessStatusCode();
        Guid machine = await PendingMachineAsync();

        using HttpRequestMessage forged = new(HttpMethod.Post, new Uri($"/api/machines/{machine}/approve", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, (await session.Http.SendAsync(forged, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.PostAsync($"/api/machines/{machine}/approve")).StatusCode);
    }

    [Fact]
    public async Task RecordsWhenAndFromWhereATokenWasLastUsedAtMostOnceAMinute()
    {
        string userName = await application.CreateUserAsync(DdtRoleNames.Viewer);
        (Guid id, string secret) = await application.SeedTokenAsync(userName, DdtRoleNames.Viewer);
        string address = TestRemoteAddress.Unique();
        using HttpClient client = application.TokenClient(secret, address);

        (await client.GetPathAsync("/api/machines")).EnsureSuccessStatusCode();
        ApiToken used = await application.TokenAsync(id);
        Assert.NotNull(used.LastUsedUtc);
        Assert.Equal(address, used.LastUsedAddress);

        DateTimeOffset recent = DateTimeOffset.UtcNow.AddSeconds(-30);
        await application.ChangeTokenAsync(id, token => token.LastUsedUtc = recent);
        (await client.GetPathAsync("/api/machines")).EnsureSuccessStatusCode();
        Assert.Equal(recent, (await application.TokenAsync(id)).LastUsedUtc);

        await application.ChangeTokenAsync(id, token => token.LastUsedUtc = DateTimeOffset.UtcNow.AddMinutes(-2));
        (await client.GetPathAsync("/api/machines")).EnsureSuccessStatusCode();
        Assert.True((await application.TokenAsync(id)).LastUsedUtc > recent);
    }

    [Fact]
    public async Task TheAuditLogNamesTheTokenThatActed()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string userName = await application.CreateUserAsync(DdtRoleNames.Operator);
        (Guid id, string secret) = await application.SeedTokenAsync(userName, DdtRoleNames.Operator, name: "build-server");
        using HttpClient client = application.TokenClient(secret);
        Guid machine = await PendingMachineAsync();

        (await client.PostJsonAsync($"/api/machines/{machine}/approve")).EnsureSuccessStatusCode();

        AuditPage page = await RegisteredMachine.ReadAsync<AuditPage>(await administrator.GetAsync($"/api/audit?subject={machine:D}&action={AuditActions.MachineApproved}"));
        AuditEntry approved = Assert.Single(page.Items);
        Assert.Equal(AuditActorKind.Token, approved.ActorKind);
        Assert.Equal($"{userName} (token build-server)", approved.ActorName);
        Assert.Equal(await application.UserIdAsync(userName), approved.ActorUserId);
        Assert.Equal(id, (await application.QueryAsync(database => database.AuditEvents.AsNoTracking().SingleAsync(
            e => e.Id == approved.Id,
            TestContext.Current.CancellationToken))).ActorTokenId);
    }

    // The owner's and the administrators' token pages get every change. Nobody else hears about the token.
    [Fact]
    public async Task PushesAChangedTokenToItsOwnerAndToAdministratorsOnly()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener administratorLive = await LiveListener.StartAsync(application, administrator);
        ChannelReader<ApiTokenView> administratorHeard = administratorLive.Listen<ApiTokenView>(LiveEvents.TokenChanged);
        string ownerName = await application.CreateUserAsync(DdtRoleNames.Viewer);
        string otherName = await application.CreateUserAsync(DdtRoleNames.Viewer);
        (_, string ownerSecret) = await application.SeedTokenAsync(ownerName, DdtRoleNames.Viewer);
        (Guid revoked, string revokedSecret) = await application.SeedTokenAsync(ownerName, DdtRoleNames.Viewer);
        (_, string otherSecret) = await application.SeedTokenAsync(otherName, DdtRoleNames.Viewer);
        Channel<ApiTokenView> ownerHeard = Channel.CreateUnbounded<ApiTokenView>();
        Channel<ApiTokenView> otherHeard = Channel.CreateUnbounded<ApiTokenView>();
        await using HubConnection ownerLive = await TokenHubAsync(ownerSecret, LiveEvents.TokenChanged, ownerHeard);
        await using HubConnection otherLive = await TokenHubAsync(otherSecret, LiveEvents.TokenChanged, otherHeard);

        // Its first use updates when it was last used, and revoking it ends it.
        using HttpClient client = application.TokenClient(revokedSecret);
        (await client.GetPathAsync("/api/machines")).EnsureSuccessStatusCode();
        Assert.NotNull((await LiveListener.NextAsync(ownerHeard.Reader, t => t.Id == revoked)).LastUsedUtc);
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{Tokens}/{revoked}")).StatusCode);

        ApiTokenView ended = await LiveListener.NextAsync(ownerHeard.Reader, t => t.Id == revoked && t.RevokedUtc is not null);
        Assert.Equal(ownerName, ended.UserName);
        Assert.NotNull(await LiveListener.NextAsync(administratorHeard, t => t.Id == revoked && t.RevokedUtc is not null));

        Guid ownerId = await application.UserIdAsync(ownerName);
        Assert.DoesNotContain(await DrainAsync(otherHeard.Reader), t => t.UserId == ownerId);
    }

    // Returns what arrived so far, after a short wait for pushes that are still on their way.
    private static async Task<List<ApiTokenView>> DrainAsync(ChannelReader<ApiTokenView> reader)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        List<ApiTokenView> drained = [];

        while (reader.TryRead(out ApiTokenView? token))
        {
            drained.Add(token);
        }

        return drained;
    }
}
