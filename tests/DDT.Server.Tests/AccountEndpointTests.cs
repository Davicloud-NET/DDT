// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using DDT.Contracts.Accounts;
using DDT.Contracts.Sequences;
using DDT.Contracts.Settings;
using DDT.Server.Accounts;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DDT.Server.Tests.AccountRequests;

namespace DDT.Server.Tests;

// The accounts steps use: everyone signed in reads them without their passwords, only an administrator signed in on the
// web and with the password entered again writes them, and a stored password goes only where it was entered for.
public sealed class AccountEndpointTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private SignedInClient? _viewer;
    private SignedInClient? _operator;

    private async Task<SignedInClient> ViewerAsync() => _viewer ??= await application.SignInAsync(DdtRoleNames.Viewer);

    private async Task<SignedInClient> OperatorAsync() => _operator ??= await application.SignInAsync(DdtRoleNames.Operator);

    private Task<List<AuditEvent>> AuditAsync(Guid accountId)
    {
        string subject = accountId.ToString("D");

        return application.QueryAsync(database => database.AuditEvents
            .AsNoTracking()
            .Where(e => e.SubjectId == subject)
            .OrderBy(e => e.Id)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<string[]> ErrorCodesAsync(HttpResponseMessage response, string field)
    {
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return [.. problem.RootElement.GetProperty("errorCodes").GetProperty(field).EnumerateArray().Select(code => code.GetProperty("code").GetString()!)];
    }

    [Fact]
    public async Task AViewerReadsAccountsButNeverTheirPasswords()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        AccountView created = await administrator.CreatedAccountAsync(Request(name: "Driver share", hosts: ["files.corp.example", "10.0.0.5"]));

        Assert.Equal("Driver share", created.Name);
        Assert.Equal(@"CORP\svc-ddt", created.UserName);
        Assert.Equal("corp.example", created.Domain);
        Assert.Equal(["files.corp.example", "10.0.0.5"], created.Hosts);
        Assert.False(created.RunAs);
        Assert.True(created.Password.IsSet);
        Assert.False(created.Password.Unreadable);
        Assert.NotNull(created.Password.UpdatedUtc);
        Assert.Empty(created.UsedBy);
        Assert.Equal(1, created.Revision);

        SignedInClient viewer = await ViewerAsync();
        HttpResponseMessage list = await viewer.GetAsync(AccountsPath);
        HttpResponseMessage one = await viewer.GetAsync($"{AccountsPath}/{created.Id:D}");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        Assert.Contains(await RegisteredMachine.ReadAsync<IReadOnlyList<AccountView>>(list), account => account.Id == created.Id);
        Assert.Equal(created.Id, (await RegisteredMachine.ReadAsync<AccountView>(one)).Id);
        Assert.DoesNotContain(Password, await list.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.DoesNotContain(Password, await one.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"{AccountsPath}/{Guid.NewGuid():D}")).StatusCode);

        // Stored encrypted for this account alone.
        Account stored = await application.QueryAsync(database => database.Accounts.AsNoTracking().SingleAsync(a => a.Id == created.Id, TestContext.Current.CancellationToken));
        AccountProtector protector = application.Services.GetRequiredService<AccountProtector>();

        Assert.DoesNotContain(Password, stored.ProtectedPassword!, StringComparison.Ordinal);
        Assert.Equal(Password, protector.Unprotect(created.Id, stored.ProtectedPassword!));
        Assert.Null(protector.Unprotect(Guid.NewGuid(), stored.ProtectedPassword!));
    }

    [Fact]
    public async Task OperatorsAndViewersCannotWriteAccounts()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        AccountView created = await administrator.CreatedAccountAsync(Request());

        foreach (SignedInClient client in new[] { await OperatorAsync(), await ViewerAsync() })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.CreateAccountAsync(Request(), null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SaveAccountAsync(created.Id, Keep(created), null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAccountAsync(created.Id, null)).StatusCode);
        }

        Assert.Equal(created.Revision, (await administrator.AccountAsync(created.Id)).Revision);
    }

    // A stolen session alone cannot change where an account goes: every write needs the password again.
    [Fact]
    public async Task AnAdministratorWritesOnlyWithThePasswordEnteredAgain()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string name = $"Unproved {Guid.NewGuid():N}";

        HttpResponseMessage unproved = await administrator.CreateAccountAsync(Request(name: name), null);
        HttpResponseMessage wrong = await administrator.CreateAccountAsync(Request(name: name), "not a token");

        Assert.Equal(HttpStatusCode.Forbidden, unproved.StatusCode);
        Assert.Equal("stepAccount.reauthenticate", await CodeAsync(unproved));
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);

        using (JsonDocument problem = JsonDocument.Parse(await unproved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)))
        {
            Assert.Equal(["account"], problem.RootElement.GetProperty("fields").EnumerateArray().Select(field => field.GetString()));
        }

        AccountView created = await administrator.CreatedAccountAsync(Request(name: name));

        Assert.Equal(HttpStatusCode.Forbidden, (await administrator.SaveAccountAsync(created.Id, Keep(created) with { RunAs = true }, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await administrator.DeleteAccountAsync(created.Id, null)).StatusCode);
        Assert.False((await administrator.AccountAsync(created.Id)).RunAs);
    }

    // A token of a script proves nobody is there, even with a proof its user got in a session.
    [Fact]
    public async Task AnApiTokenCannotWriteAccounts()
    {
        SignedInClient person = await application.SignInAsync(DdtRoleNames.Administrator);
        string userName = (await RegisteredMachine.ReadAsync<CurrentUserName>(await person.GetAsync("/api/auth/me"))).UserName;
        string proof = await person.TokenAsync();
        (_, string secret) = await application.SeedTokenAsync(userName, DdtRoleNames.Administrator);
        using HttpClient script = application.TokenClient(secret, TestRemoteAddress.Unique());
        AccountView created = await person.CreatedAccountAsync(Request());

        Assert.Equal(HttpStatusCode.OK, (await script.GetPathAsync(AccountsPath)).StatusCode);

        using HttpRequestMessage create = new(HttpMethod.Post, new Uri(AccountsPath, UriKind.Relative))
        {
            Content = JsonContent.Create(Request(), options: TestJson.Options),
        };
        create.Headers.Add(Server.Settings.ReauthenticationTokens.HeaderName, proof);
        HttpResponseMessage byToken = await script.SendAsync(create, TestContext.Current.CancellationToken);

        using HttpRequestMessage save = new(HttpMethod.Put, new Uri($"{AccountsPath}/{created.Id:D}", UriKind.Relative))
        {
            Content = JsonContent.Create(Keep(created) with { RunAs = true }, options: TestJson.Options),
        };
        save.Headers.Add(Server.Settings.ReauthenticationTokens.HeaderName, proof);
        HttpResponseMessage saved = await script.SendAsync(save, TestContext.Current.CancellationToken);

        using HttpRequestMessage delete = new(HttpMethod.Delete, new Uri($"{AccountsPath}/{created.Id:D}", UriKind.Relative));
        delete.Headers.Add(Server.Settings.ReauthenticationTokens.HeaderName, proof);
        HttpResponseMessage deleted = await script.SendAsync(delete, TestContext.Current.CancellationToken);

        Assert.All([byToken, saved, deleted], response => Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));
        Assert.Equal("stepAccount.apiToken", await CodeAsync(byToken));
        Assert.Equal("stepAccount.apiToken", await CodeAsync(saved));
        Assert.False((await person.AccountAsync(created.Id)).RunAs);
    }

    // settings.md 5.4: a stored password goes only to the user name, domain and servers it was entered for.
    [Fact]
    public async Task KeepingThePasswordForANewDestinationIsRefusedAndAudited()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string proof = await administrator.TokenAsync();
        AccountView account = await administrator.CreatedAccountAsync(Request(hosts: ["files.corp.example", "backup.corp.example"]));

        SaveAccountRequest[] refused =
        [
            Keep(account) with { UserName = @"CORP\domain-admin" },
            Keep(account) with { Domain = "evil.example" },
            Keep(account) with { Domain = null },
            Keep(account) with { Hosts = [.. account.Hosts, "evil.example"] },
        ];

        foreach (SaveAccountRequest request in refused)
        {
            HttpResponseMessage response = await administrator.SaveAccountAsync(account.Id, request, proof);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(["stepAccount.passwordForNewDestination"], await ErrorCodesAsync(response, "password"));
        }

        Assert.Equal(account.Revision, (await administrator.AccountAsync(account.Id)).Revision);

        List<AuditEvent> audit = await AuditAsync(account.Id);
        List<AuditEvent> refusals = [.. audit.Where(e => e.Action == AuditActions.AccountRefused)];

        Assert.Equal(4, refusals.Count);
        Assert.EndsWith("for a new destination: userName.", refusals[0].Detail, StringComparison.Ordinal);
        Assert.EndsWith("for a new destination: domain.", refusals[1].Detail, StringComparison.Ordinal);
        Assert.EndsWith("for a new destination: hosts.", refusals[3].Detail, StringComparison.Ordinal);
        Assert.All(audit, e => Assert.DoesNotContain(Password, e.Detail ?? "", StringComparison.Ordinal));

        // Fewer servers, the same names in another case, or the password entered again, reach nothing it was not entered for.
        AccountView fewer = await RegisteredMachine.ReadAsync<AccountView>(
            await administrator.SaveAccountAsync(account.Id, Keep(account) with { Hosts = ["files.corp.example"], UserName = @"corp\SVC-DDT" }, proof));

        Assert.Equal(["files.corp.example"], fewer.Hosts);
        Assert.Equal(@"corp\SVC-DDT", fewer.UserName);
        Assert.True(fewer.Password.IsSet);
        Assert.Equal(account.Revision + 1, fewer.Revision);

        AccountView moved = await RegisteredMachine.ReadAsync<AccountView>(await administrator.SaveAccountAsync(
            account.Id,
            Keep(fewer) with { Hosts = ["evil.example"], Password = new SecretUpdate(SecretAction.Set, "Entered again 8") },
            proof));

        Assert.Equal(["evil.example"], moved.Hosts);
        Assert.True(moved.Password.IsSet);

        Account stored = await application.QueryAsync(database => database.Accounts.AsNoTracking().SingleAsync(a => a.Id == account.Id, TestContext.Current.CancellationToken));
        Assert.Equal("Entered again 8", application.Services.GetRequiredService<AccountProtector>().Unprotect(account.Id, stored.ProtectedPassword!));

        List<string?> changes = [.. (await AuditAsync(account.Id)).Where(e => e.Action == AuditActions.AccountChanged).Select(e => e.Detail)];
        Assert.Equal(2, changes.Count);
        Assert.Contains("hosts removed backup.corp.example", changes[0], StringComparison.Ordinal);
        Assert.Contains("hosts added evil.example", changes[1], StringComparison.Ordinal);
        Assert.Contains("password set", changes[1], StringComparison.Ordinal);
        Assert.All(changes, detail => Assert.DoesNotContain("Entered again 8", detail!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task APasswordIsClearedSetAndKeptWhereNothingElseChanges()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string proof = await administrator.TokenAsync();
        AccountView account = await administrator.CreatedAccountAsync(Request(password: null));

        Assert.False(account.Password.IsSet);
        Assert.Null(account.Password.UpdatedUtc);

        // Nothing to keep: a new destination needs no password it never had.
        AccountView moved = await RegisteredMachine.ReadAsync<AccountView>(
            await administrator.SaveAccountAsync(account.Id, Keep(account) with { Domain = "lab.example" }, proof));
        Assert.Equal("lab.example", moved.Domain);

        AccountView set = await RegisteredMachine.ReadAsync<AccountView>(await administrator.SaveAccountAsync(
            account.Id,
            Keep(moved) with { Password = new SecretUpdate(SecretAction.Set, "First 1") },
            proof));
        Assert.True(set.Password.IsSet);

        // The same save again changes nothing and records nothing.
        HttpResponseMessage same = await administrator.SaveAccountAsync(account.Id, Keep(set), proof);
        Assert.Equal(set.Revision, (await RegisteredMachine.ReadAsync<AccountView>(same)).Revision);

        AccountView cleared = await RegisteredMachine.ReadAsync<AccountView>(await administrator.SaveAccountAsync(
            account.Id,
            Keep(set) with { Password = new SecretUpdate(SecretAction.Clear, null) },
            proof));
        Assert.False(cleared.Password.IsSet);
        Assert.Null(await application.QueryAsync(database =>
            database.Accounts.Where(a => a.Id == account.Id).Select(a => a.ProtectedPassword).SingleAsync(TestContext.Current.CancellationToken)));

        List<string?> changes = [.. (await AuditAsync(account.Id)).Select(e => e.Action + " " + e.Detail)];
        Assert.Equal(4, changes.Count);
        Assert.EndsWith("password cleared.", changes[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASaveOverANewerOneGetsTheNewerView()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string proof = await administrator.TokenAsync();
        AccountView account = await administrator.CreatedAccountAsync(Request());
        AccountView newer = await RegisteredMachine.ReadAsync<AccountView>(
            await administrator.SaveAccountAsync(account.Id, Keep(account) with { RunAs = true }, proof));

        HttpResponseMessage stale = await administrator.SaveAccountAsync(account.Id, Keep(account) with { Name = "Mine" }, proof);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        AccountView current = (await stale.Content.ReadFromJsonAsync<AccountView>(TestJson.Options, TestContext.Current.CancellationToken))!;
        Assert.Equal(newer.Revision, current.Revision);
        Assert.True(current.RunAs);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.SaveAccountAsync(Guid.NewGuid(), Keep(account), proof)).StatusCode);
    }

    [Fact]
    public async Task TheFieldsAreChecked()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string proof = await administrator.TokenAsync();
        AccountView taken = await administrator.CreatedAccountAsync(Request());

        HttpResponseMessage response = await administrator.CreateAccountAsync(
            Request(
                name: taken.Name.ToUpperInvariant(),
                userName: "svc-ddt",
                domain: "not a domain",
                hosts: ["files.corp.example", "FILES.corp.example", "files@SSL", ""],
                password: new string('x', AccountLimits.MaxPasswordLength + 1)),
            proof);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["stepAccount.nameTaken"], await ErrorCodesAsync(response, "name"));
        Assert.Equal(["stepAccount.userNameForm"], await ErrorCodesAsync(response, "userName"));
        Assert.Equal(["stepAccount.domainInvalid"], await ErrorCodesAsync(response, "domain"));
        Assert.Equal(["stepAccount.hostRepeated"], await ErrorCodesAsync(response, "hosts[1]"));
        Assert.Equal(["stepAccount.hostInvalid"], await ErrorCodesAsync(response, "hosts[2]"));
        Assert.Equal(["stepAccount.hostInvalid"], await ErrorCodesAsync(response, "hosts[3]"));
        Assert.Equal(["stepAccount.passwordLength"], await ErrorCodesAsync(response, "password"));

        HttpResponseMessage many = await administrator.CreateAccountAsync(
            Request(hosts: [.. Enumerable.Range(0, AccountLimits.MaxHosts + 1).Select(n => $"files{n}.corp.example")]),
            proof);
        Assert.Equal(["stepAccount.tooManyHosts"], await ErrorCodesAsync(many, "hosts"));

        // Both forms of a qualified name, and no domain at all, are fine.
        Assert.Equal(HttpStatusCode.Created, (await administrator.CreateAccountAsync(Request(userName: "svc@corp.example", domain: null, hosts: []), proof)).StatusCode);
    }

    // A run of a sequence that names it would fail at the step, after the disk was erased.
    [Fact]
    public async Task AnAccountASequenceUsesCannotBeDeleted()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string proof = await administrator.TokenAsync();
        AccountView account = await administrator.CreatedAccountAsync(Request(runAs: true));
        RunScriptStep script = Script(account.Id);
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Definition(
            new GroupStep { Id = Guid.NewGuid(), Name = "Tools", Steps = [script] }));

        AccountUse use = Assert.Single((await administrator.AccountAsync(account.Id)).UsedBy);
        Assert.Equal(sequence.Id, use.SequenceId);
        Assert.Equal(sequence.Name, use.SequenceName);
        Assert.Equal(
            [new AccountStepUse(script.Id, script.Name, "runAs"), new AccountStepUse(script.Id, script.Name, "shares[0].account")],
            use.Steps!);

        HttpResponseMessage refused = await administrator.DeleteAccountAsync(account.Id, proof);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("stepAccount.inUse", await CodeAsync(refused));
        Assert.Equal(
            $"The sequence {sequence.Name} uses this account. Choose another account there first.",
            await TestDatabase.TitleAsync(refused));

        (await administrator.SaveSequenceAsync(sequence, SequenceRequests.ScriptOnly())).EnsureSuccessStatusCode();
        Assert.Empty((await administrator.AccountAsync(account.Id)).UsedBy);

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAccountAsync(account.Id, proof)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.GetAsync($"{AccountsPath}/{account.Id:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.DeleteAccountAsync(account.Id, proof)).StatusCode);
        Assert.Equal(
            [AuditActions.AccountCreated, AuditActions.AccountDeleted],
            (await AuditAsync(account.Id)).Select(e => e.Action));
    }

    // Every page that shows accounts takes the change from the hub, and no payload holds a password.
    [Fact]
    public async Task ChangesReachTheHubWithoutPasswords()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string proof = await administrator.TokenAsync();
        await using LiveListener listener = await LiveListener.StartAsync(application, await ViewerAsync());
        ChannelReader<JsonElement> changed = listener.Listen<JsonElement>(LiveEvents.AccountChanged);
        ChannelReader<JsonElement> removed = listener.Listen<JsonElement>(LiveEvents.AccountsRemoved);

        AccountView account = await administrator.CreatedAccountAsync(Request(name: $"Pushed {Guid.NewGuid():N}", runAs: true));
        JsonElement pushed = await LiveListener.NextAsync(changed, e => e.GetProperty("id").GetGuid() == account.Id);

        Assert.True(pushed.GetProperty("password").GetProperty("isSet").GetBoolean());
        Assert.DoesNotContain(Password, pushed.GetRawText(), StringComparison.Ordinal);

        // A sequence that starts naming the account changes what it is used by.
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Definition(Script(account.Id)));
        JsonElement used = await LiveListener.NextAsync(changed, e => e.GetProperty("id").GetGuid() == account.Id && e.GetProperty("usedBy").GetArrayLength() == 1);

        Assert.Equal(sequence.Id, used.GetProperty("usedBy")[0].GetProperty("sequenceId").GetGuid());

        (await administrator.SaveSequenceAsync(sequence, SequenceRequests.ScriptOnly())).EnsureSuccessStatusCode();
        await LiveListener.NextAsync(changed, e => e.GetProperty("id").GetGuid() == account.Id && e.GetProperty("usedBy").GetArrayLength() == 0);

        AccountView current = await administrator.AccountAsync(account.Id);
        (await administrator.SaveAccountAsync(account.Id, Keep(current) with { Password = new SecretUpdate(SecretAction.Set, "Sent nowhere 9") }, proof))
            .EnsureSuccessStatusCode();
        JsonElement set = await LiveListener.NextAsync(changed, e => e.GetProperty("id").GetGuid() == account.Id && e.GetProperty("revision").GetInt64() == 2);

        Assert.DoesNotContain("Sent nowhere 9", set.GetRawText(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAccountAsync(account.Id, proof)).StatusCode);
        JsonElement gone = await LiveListener.NextAsync(removed);

        Assert.Equal([account.Id], gone.GetProperty("accountIds").EnumerateArray().Select(id => id.GetGuid()));
    }

    // Moving the key ring leaves a password that no longer decrypts: it shows so, and keeping it is refused.
    [Fact]
    public async Task APasswordThatNoLongerDecryptsIsShownAndCannotBeKept()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        AccountView account = await administrator.CreatedAccountAsync(Request(runAs: true));
        string foreign = application.Services.GetRequiredService<AccountProtector>().Protect(Guid.NewGuid(), Password);

        await application.QueryAsync(database => database.Accounts
            .Where(a => a.Id == account.Id)
            .ExecuteUpdateAsync(a => a.SetProperty(x => x.ProtectedPassword, foreign), TestContext.Current.CancellationToken));

        AccountView unreadable = await administrator.AccountAsync(account.Id);
        Assert.False(unreadable.Password.IsSet);
        Assert.True(unreadable.Password.Unreadable);

        HttpResponseMessage kept = await administrator.SaveAccountAsync(account.Id, Keep(unreadable) with { Name = "Renamed" }, await administrator.TokenAsync());
        Assert.Equal(["stepAccount.passwordUnreadable"], await ErrorCodesAsync(kept, "password"));
    }

    // A script in Windows that runs as the account and connects a share with it.
    private static RunScriptStep Script(Guid accountId) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Copy tools",
        Phase = SequencePhase.Windows,
        Interpreter = ScriptInterpreter.Cmd,
        Script = "echo copied",
        RunAs = new AccountReference(accountId, null),
        Shares = [new ShareConnection(@"\\files.corp.example\tools", new AccountReference(accountId, null))],
    };

    private sealed record CurrentUserName(string UserName);
}
