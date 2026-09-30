// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Authentication;
using DDT.Contracts.Settings;
using DDT.Host.Startup;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// Recovery without the page.
// The verbs run next to the running server, against its store, and the server applies what they wrote at its next poll.
public sealed class SettingsConsoleTests
{
    [Fact]
    public async Task ResetWritesTheDefaultsAndClearsTheSecrets()
    {
        using DdtApplication application = new();
        SignedInClient administrator = await application.AdministratorAsync();
        await administrator.SavedAsync<LdapSettings>(
            SettingsSectionNames.Ldap,
            values => values with { Enabled = true, Host = "dc1.corp.example", BaseDn = "dc=corp,dc=example" },
            new(
                Secrets: new Dictionary<string, SecretUpdate> { ["bindPassword"] = new(SecretAction.Set, "Bind password 7") },
                Reauthentication: await administrator.TokenAsync()));
        StringWriter output = new();

        int exit = await SettingsConsole.RunAsync(["settings", "reset", "ldap"], output, Store(application));

        Assert.Equal(0, exit);
        Assert.StartsWith("Reset ldap to the defaults and cleared its secrets.", output.ToString(), StringComparison.Ordinal);

        await application.RefreshSettingsAsync();

        SettingsSectionView<LdapSettings> view = await administrator.SectionAsync<LdapSettings>(SettingsSectionNames.Ldap);
        Assert.False(view.Values.Enabled);
        Assert.Equal(string.Empty, view.Values.Host);
        Assert.False(view.Secrets["bindPassword"].IsSet);
        Assert.Equal("console", view.UpdatedBy);
        Assert.True(await application.QueryAsync(database => database.AuditEvents.AnyAsync(
            audit => audit.Action == AuditActions.SettingsReset && audit.ActorName == "console" && audit.SubjectId == SettingsSectionNames.Ldap,
            TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task CreateAdminMakesAnAdministratorOrEnablesItAgain()
    {
        using DdtApplication application = new();
        await application.AdministratorAsync();
        string userName = $"rescue-{Guid.NewGuid():N}";
        StringWriter created = new();

        Assert.Equal(0, await SettingsConsole.RunAsync(["settings", "create-admin", userName], created, Store(application)));
        string firstPassword = PasswordIn(created.ToString());
        Assert.Equal(LoginStatus.Succeeded, await SignInAsync(application, userName, firstPassword));

        using (IServiceScope scope = application.Services.CreateScope())
        {
            UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
            DdtUser user = (await users.FindByNameAsync(userName))!;
            user.IsDisabled = true;
            Assert.True((await users.UpdateAsync(user)).Succeeded);
        }

        StringWriter reset = new();
        Assert.Equal(0, await SettingsConsole.RunAsync(["settings", "create-admin", userName], reset, Store(application)));
        string secondPassword = PasswordIn(reset.ToString());

        Assert.NotEqual(firstPassword, secondPassword);
        Assert.Equal(LoginStatus.Succeeded, await SignInAsync(application, userName, secondPassword));
        Assert.Equal(2, await application.QueryAsync(database => database.AuditEvents.CountAsync(
            audit => audit.Action == AuditActions.AdministratorCreated && audit.ActorName == "console",
            TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task AnUnknownVerbPrintsTheUsage()
    {
        StringWriter output = new();

        Assert.Equal(2, await SettingsConsole.RunAsync(["settings", "reset", "everything"], output));
        Assert.StartsWith("Usage: DDT.Host settings reset <section>", output.ToString(), StringComparison.Ordinal);
    }

    // What setup's netboot settings are
    [Fact]
    public async Task NetbootInterfaceSetsTheInterfacesOnlyWhileThereAreNone()
    {
        using DdtApplication application = new();
        SignedInClient administrator = await application.AdministratorAsync();
        StringWriter first = new();
        StringWriter second = new();

        Assert.Equal(0, await SettingsConsole.RunAsync(["settings", "netboot-interface", "Ethernet"], first, Store(application)));
        Assert.Equal(0, await SettingsConsole.RunAsync(["settings", "netboot-interface", "Wi-Fi"], second, Store(application)));

        await application.RefreshSettingsAsync();
        SettingsSectionView<PxeSettings> view = await administrator.SectionAsync<PxeSettings>(SettingsSectionNames.Pxe);
        Assert.Equal(["Ethernet"], view.Values.Interfaces);
        Assert.StartsWith("PXE serves Ethernet now.", first.ToString(), StringComparison.Ordinal);
        Assert.StartsWith("PXE already serves Ethernet", second.ToString(), StringComparison.Ordinal);
    }

    private static Dictionary<string, string?> Store(DdtApplication application) => new()
    {
        ["DDT:StorePath"] = application.StorePath,
        ["ConnectionStrings:ddtdb"] = string.Empty,
    };

    private static string PasswordIn(string output)
    {
        const string Marker = "with the password ";
        int start = output.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length;

        return output[start..output.IndexOf('.', start)];
    }

    private static async Task<LoginStatus?> SignInAsync(DdtApplication application, string userName, string password)
    {
        System.Net.CookieContainer cookies = new();
        using SignedInClient browser = new(application.CreateDefaultClient(new CookieContainerHandler(cookies)), cookies);

        return await browser.SignInAsync(userName, password);
    }
}
