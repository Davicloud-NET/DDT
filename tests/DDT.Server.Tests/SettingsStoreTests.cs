// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using DDT.Contracts.Settings;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

public sealed class SettingsStoreTests
{
    private const string JoinPassword = "Join the domain 7";

    // Each field configuration sets and the store never had is written once, secrets encrypted on the way in, and named
    // in one audit row. A code default is never written.
    [Fact]
    public async Task ConfiguredValuesAreImportedFieldByField()
    {
        using SettingsApplication application = new(
            ("DDT:Deployment:TimeZone", "W. Europe Standard Time"),
            ("DDT:Deployment:Domain:Password", JoinPassword));
        await application.AdministratorAsync();

        SettingsSection row = await Row(application, SettingsSectionNames.Deployment);
        JsonObject values = JsonNode.Parse(row.Values)!.AsObject();

        Assert.Equal("W. Europe Standard Time", values["timeZone"]!.GetValue<string>());
        Assert.False(values.ContainsKey("locale"));
        Assert.False(values.ContainsKey("localAdministrator"));
        Assert.Contains("domain.password", row.Secrets, StringComparison.Ordinal);
        Assert.DoesNotContain(JoinPassword, row.Secrets, StringComparison.Ordinal);
        Assert.Equal(1, row.Version);
        Assert.Equal("configuration", row.UpdatedByName);

        string? detail = await application.QueryAsync(database => database.AuditEvents
            .Where(audit => audit.Action == AuditActions.SettingsImported && audit.SubjectId == SettingsSectionNames.Deployment)
            .Select(audit => audit.Detail)
            .SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Imported into deployment from configuration: timeZone; domain.password", detail);

        // Nothing configured, nothing written.
        Assert.Null(await application.QueryAsync(database => database.SettingsSections.SingleOrDefaultAsync(r => r.Section == SettingsSectionNames.Ldap, TestContext.Current.CancellationToken)));
    }

    // A field written once is never imported again, so a process that starts later with other values does not overwrite
    // what the page holds: its configured value still applies to it, as a lock.
    [Fact]
    public async Task AFieldIsImportedOnlyWhileItWasNeverWritten()
    {
        using SettingsApplication application = new(("DDT:Machines:MaxWaiting", "500"));
        await application.AdministratorAsync();

        using (IServiceScope scope = application.Services.CreateScope())
        {
            SettingsStore store = scope.ServiceProvider.GetRequiredService<SettingsStore>();
            await store.ImportAsync(TestContext.Current.CancellationToken);
        }

        SettingsSection row = await Row(application, SettingsSectionNames.Machines);

        Assert.Equal(1, row.Version);
        Assert.Equal(1, await application.QueryAsync(database => database.AuditEvents.CountAsync(audit => audit.Action == AuditActions.SettingsImported, TestContext.Current.CancellationToken)));
    }

    // The purpose names section and field, so a ciphertext copied into another field does not decrypt there.
    [Fact]
    public async Task ACiphertextDecryptsOnlyInItsOwnField()
    {
        using SettingsApplication application = new(("DDT:Deployment:Domain:Password", JoinPassword));
        await application.AdministratorAsync();
        SettingsProtector protector = application.Services.GetRequiredService<SettingsProtector>();
        SettingsSection row = await Row(application, SettingsSectionNames.Deployment);

        StoredSettingsSection stored = protector.Decode(row);
        Assert.Equal(JoinPassword, stored.Secrets["domain.password"].Value);

        row.Secrets = row.Secrets.Replace("domain.password", "localAdministrator.password", StringComparison.Ordinal);
        StoredSettingsSection moved = protector.Decode(row);

        Assert.True(moved.Secrets["localAdministrator.password"].Unreadable);
        Assert.Null(moved.Secrets["localAdministrator.password"].Value);
    }

    // Every process on one database has to share the key ring; one that does not refuses to save.
    [Fact]
    public async Task AProcessWithAnotherKeyRingSavesNothing()
    {
        using DdtApplication application = new();
        SignedInClient administrator = await application.AdministratorAsync();

        using (IServiceScope scope = application.Services.CreateScope())
        {
            DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
            SettingsSection keyRing = await database.SettingsSections.SingleAsync(r => r.Section == SettingsSectionNames.KeyRing, TestContext.Current.CancellationToken);
            keyRing.Secrets = new SettingsProtector(new EphemeralDataProtectionProvider()).CanarySecrets(DateTimeOffset.UtcNow);
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);

            Assert.False(await scope.ServiceProvider.GetRequiredService<SettingsStore>().ImportAsync(TestContext.Current.CancellationToken));
        }

        SettingsSectionView<LoggingSettings> loaded = await administrator.SectionAsync<LoggingSettings>(SettingsSectionNames.Logging);
        HttpResponseMessage refused = await administrator.SaveAsync(
            SettingsSectionNames.Logging,
            loaded.Version,
            loaded.Values with { LogLevel = new Dictionary<string, string> { ["Default"] = "Debug" } });

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.StartsWith("This server cannot read the key ring", await TestDatabase.TitleAsync(refused), StringComparison.Ordinal);
        Assert.False((await RegisteredMachine.ReadAsync<SettingsOverview>(await administrator.GetAsync("/api/settings"))).KeyRingReadable);
    }

    // Another process's save reaches this one at its next poll, and its browsers as a push.
    [Fact]
    public async Task ASaveOfAnotherProcessIsReadAtThePoll()
    {
        using DdtApplication application = new();
        await application.AdministratorAsync();

        using (IServiceScope scope = application.Services.CreateScope())
        {
            DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
            database.SettingsSections.Add(new SettingsSection
            {
                Section = SettingsSectionNames.Machines,
                SchemaVersion = SettingsStore.SchemaVersion,
                Values = """{"maxWaitingPerAddress":7}""",
                Version = 4,
                UpdatedUtc = DateTimeOffset.UtcNow,
                UpdatedByName = "another process",
            });
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        DdtSettings settings = application.Services.GetRequiredService<DdtSettings>();
        Assert.Equal(100, settings.Current.Machines.MaxWaitingPerAddress);

        await application.RefreshSettingsAsync();

        Assert.Equal(7, settings.Current.Machines.MaxWaitingPerAddress);
        Assert.Equal(4, settings.Current[SettingsSectionNames.Machines].Version);
        Assert.Equal("another process", settings.Current[SettingsSectionNames.Machines].UpdatedBy);
    }

    // A stored value a newer rule refuses does not stop the server; the section fails closed and lists why, and new runs
    // are refused until the page fixes it.
    [Fact]
    public async Task StoredDeploymentProblemsRefuseNewRuns()
    {
        using DdtApplication application = new();
        SignedInClient administrator = await application.AdministratorAsync();

        using (IServiceScope scope = application.Services.CreateScope())
        {
            DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
            database.SettingsSections.Add(new SettingsSection
            {
                Section = SettingsSectionNames.Deployment,
                SchemaVersion = SettingsStore.SchemaVersion,
                Values = """{"timeZone":"Europe/Berlin"}""",
                Version = 1,
            });
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await application.RefreshSettingsAsync();

        SettingsSectionView<DeploymentSettings> view = await administrator.SectionAsync<DeploymentSettings>(SettingsSectionNames.Deployment);
        SettingMessage problem = Assert.Single(view.Problems);

        Assert.Equal("timeZone", problem.Field);
        Assert.Null(problem.Code);
        Assert.Equal("Europe/Berlin", view.Values.TimeZone);
        Assert.StartsWith(
            "The deployment settings have problems, so no run starts until an administrator fixes them on the settings page: timeZone: 'Europe/Berlin'",
            DDT.Server.Deployments.DeploymentService.SettingsProblem(application.Services.GetRequiredService<DdtSettings>().Current),
            StringComparison.Ordinal);
    }

    private static Task<SettingsSection> Row(DdtApplication application, string section) =>
        application.QueryAsync(database => database.SettingsSections.AsNoTracking().SingleAsync(row => row.Section == section, TestContext.Current.CancellationToken));
}
