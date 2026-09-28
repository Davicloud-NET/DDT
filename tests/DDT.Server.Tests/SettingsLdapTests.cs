// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Authentication;
using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Server.Authentication;
using DDT.Server.Ldap;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DDT.Server.Tests;

// The LDAP test tries the values of the form before they are saved, with a stored bind password only against the server
// it was entered for.
public sealed class SettingsLdapTests(SettingsLdapTests.TesterApplication application) : IClassFixture<SettingsLdapTests.TesterApplication>
{
    private const string BindPassword = "Bind password 7";

    [Fact]
    public async Task TheTestUsesTheValuesOfTheFormAndMapsTheGroups()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        application.Tester.Groups = ["CN=DDT Admins,DC=corp", "CN=Other,DC=corp"];

        LdapTestResult result = await TestAsync(administrator, Values("dc9.corp.example"), new(SecretAction.Set, "typed password"), "jane", "Her password 1");

        Assert.True(result.Bound);
        Assert.True(result.UserFound);
        Assert.True(result.PasswordAccepted);
        Assert.Equal(["CN=DDT Admins,DC=corp", "CN=Other,DC=corp"], result.Groups);
        Assert.Equal(DdtRoleNames.Administrator, result.Role);
        Assert.Equal("cn=jane was found, in 2 groups. The group map makes the account Administrator.", result.Message);
        Assert.Equal(ServerMessages.SettingsLdapTestRole.Code, result.Text?.Code);
        Assert.Null(result.Proof);
        Assert.Equal("dc9.corp.example", application.Tester.Options!.Host);
        Assert.Equal("typed password", application.Tester.Options.BindPassword);
        Assert.Equal("jane", application.Tester.UserName);
    }

    [Fact]
    public async Task AStoredBindPasswordIsUsedOnlyAgainstItsServer()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string token = await administrator.TokenAsync();
        await administrator.SavedAsync<LdapSettings>(
            SettingsSectionNames.Ldap,
            _ => Values("dc1.corp.example"),
            new(
                Secrets: new Dictionary<string, SecretUpdate> { ["bindPassword"] = new(SecretAction.Set, BindPassword) },
                Confirm: [SettingWarningCodes.LdapNoAdministrator],
                Reauthentication: token));

        await TestAsync(administrator, Values("dc1.corp.example"), new(SecretAction.Keep, null), null, null);
        Assert.Equal(BindPassword, application.Tester.Options!.BindPassword);

        HttpResponseMessage refused = await administrator.PostAsync(
            "/api/settings/ldap/test",
            new LdapTestRequest(Values("attacker.example"), null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("bindPassword", (await SettingsRequests.ProblemsAsync(refused)).Errors.Keys);

        // A save that keeps the password for another server is refused the same way, and audited.
        SettingsSectionView<LdapSettings> stored = await administrator.SectionAsync<LdapSettings>(SettingsSectionNames.Ldap);
        HttpResponseMessage save = await administrator.SaveAsync(
            SettingsSectionNames.Ldap,
            stored.Version,
            stored.Values with { Host = "attacker.example" },
            new(Reauthentication: token));

        Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
        Assert.Contains("bindPassword", (await SettingsRequests.ProblemsAsync(save)).Errors.Keys);
        Assert.True(await application.QueryAsync(database => database.AuditEvents.AnyAsync(
            audit => audit.Action == AuditActions.SettingsRefused && audit.Detail!.Contains("bindPassword"),
            TestContext.Current.CancellationToken)));
    }

    // The user part goes through the lockout of a directory sign-in: a wrong password counts, and a local account's
    // password is never sent to the directory.
    [Fact]
    public async Task TheUserPartKeepsToTheRulesOfASignIn()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string local = await application.CreateUserAsync(DdtRoleNames.Viewer);

        LdapTestResult localResult = await TestAsync(administrator, Values("dc1.corp.example"), new(SecretAction.Set, "x"), local, "Her password 1");

        Assert.Null(localResult.PasswordAccepted);
        Assert.Equal($"{local} is not a directory account, so its password is not sent to the directory.", localResult.Message);
        Assert.Equal(ServerMessages.SettingsLdapTestNotDirectoryAccount.Code, localResult.Text?.Code);
        Assert.NotEqual(local, application.Tester.UserName);
    }

    // Without the proof of its own test, a directory administrator could save values that take away their role.
    [Fact]
    public async Task ADirectoryAdministratorSavesNewDirectoryValuesOnlyWithTheProofOfATest()
    {
        using DirectoryApplication directory = new();
        SignedInClient administrator = await directory.AdministratorAsync();
        SignedInClient directoryAdministrator = await SignInAsDirectoryAdministratorAsync(directory);
        string token = await directoryAdministrator.TokenAsync();
        SettingsSectionView<LdapSettings> loaded = await directoryAdministrator.SectionAsync<LdapSettings>(SettingsSectionNames.Ldap);

        HttpResponseMessage refused = await directoryAdministrator.SaveAsync(
            SettingsSectionNames.Ldap,
            loaded.Version,
            loaded.Values with { UserFilter = "(&(objectClass=person)(uid={0}))" },
            new(Reauthentication: token));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.StartsWith("You sign in through the directory", Assert.Single((await SettingsRequests.ProblemsAsync(refused)).Errors[string.Empty]), StringComparison.Ordinal);

        // A local administrator needs no such proof.
        SettingsSectionView<LdapSettings> other = await administrator.SectionAsync<LdapSettings>(SettingsSectionNames.Ldap);
        Assert.Equal(HttpStatusCode.OK, (await administrator.SaveAsync(
            SettingsSectionNames.Ldap,
            other.Version,
            other.Values with { UserFilter = "(&(objectClass=person)(uid={0}))" },
            new(Reauthentication: await administrator.TokenAsync()))).StatusCode);
    }

    // Directory accounts in the administrators group of FakeLdapAuthenticator are administrators.
    private static async Task<SignedInClient> SignInAsDirectoryAdministratorAsync(DirectoryApplication directory)
    {
        string userName = $"admin-{Guid.NewGuid():N}";
        directory.Ldap.Groups[userName] = [FakeLdapAuthenticator.AdministratorsGroup];
        CookieContainer cookies = new();
        SignedInClient browser = new(directory.CreateDefaultClient(new CookieContainerHandler(cookies)), cookies);

        Assert.Equal(LoginStatus.Succeeded, await browser.SignInAsync(userName, DdtApplication.Password));

        return browser;
    }

    private static async Task<LdapTestResult> TestAsync(SignedInClient client, LdapSettings values, SecretUpdate secret, string? userName, string? password) =>
        await RegisteredMachine.ReadAsync<LdapTestResult>(await client.PostAsync(
            "/api/settings/ldap/test",
            new LdapTestRequest(values, new Dictionary<string, SecretUpdate> { ["bindPassword"] = secret }, userName, password)));

    private static LdapSettings Values(string host) => new(
        false,
        host,
        636,
        DirectoryTransport.Ldaps,
        "dc=corp,dc=example",
        "cn=ddt,dc=corp,dc=example",
        "(&(objectClass=user)(sAMAccountName={0}))",
        "objectGUID",
        "displayName",
        "mail",
        true,
        new Dictionary<string, string> { ["CN=DDT Admins,DC=corp"] = DdtRoleNames.Administrator, ["CN=Other,DC=corp"] = DdtRoleNames.Viewer },
        TimeSpan.FromSeconds(10));

    public sealed class TesterApplication : DdtApplication
    {
        public FakeLdapTester Tester { get; } = new();

        protected override void ConfigureTestHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ILdapTester>();
                services.AddSingleton<ILdapTester>(Tester);
            });
        }
    }

    // A directory in which every user exists with every password, in the groups a test sets.
    public sealed class FakeLdapTester : ILdapTester
    {
        public IReadOnlyList<string> Groups { get; set; } = [];

        public LdapOptions? Options { get; private set; }

        public string? UserName { get; private set; }

        public Task<LdapTestOutcome> TestAsync(LdapOptions options, string? userName, string? password, CancellationToken cancellationToken)
        {
            Options = options;
            UserName = userName;

            return Task.FromResult(userName is null
                ? new LdapTestOutcome(true, null, null, [], ServerMessages.SettingsLdapTestBound.With("account", options.BindDn, "server", options.Host))
                : new LdapTestOutcome(
                    true,
                    true,
                    password is null ? null : true,
                    Groups,
                    ServerMessages.SettingsLdapTestFound.With("entry", $"cn={userName}", "count", Groups.Count)));
        }
    }
}
