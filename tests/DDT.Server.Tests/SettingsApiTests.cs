// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Deployments;
using DDT.Contracts.Settings;
using DDT.Server.Authentication;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

public sealed class SettingsApiTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    [Fact]
    public async Task ASaveAnswersWithTheSectionAndIsAudited()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SettingsSectionView<DeploymentSettings> before = await administrator.SectionAsync<DeploymentSettings>(SettingsSectionNames.Deployment);

        HttpResponseMessage response = await administrator.SaveAsync(
            SettingsSectionNames.Deployment,
            before.Version,
            before.Values with { TimeZone = "W. Europe Standard Time", Keyboard = "de-DE" });
        SettingsSectionView<DeploymentSettings> after = await RegisteredMachine.ReadAsync<SettingsSectionView<DeploymentSettings>>(response);

        Assert.Equal(before.Version + 1, after.Version);
        Assert.Equal("W. Europe Standard Time", after.Values.TimeZone);
        Assert.Equal("de-DE", after.Values.Keyboard);
        Assert.NotNull(after.UpdatedBy);
        Assert.Null(after.Apply);
        Assert.Empty(after.Reauthenticate);
        Assert.Equal("W. Europe Standard Time", (await administrator.SectionAsync<DeploymentSettings>(SettingsSectionNames.Deployment)).Values.TimeZone);

        string? detail = await application.QueryAsync(database => database.AuditEvents
            .Where(audit => audit.Action == AuditActions.SettingsChanged && audit.SubjectId == SettingsSectionNames.Deployment)
            .OrderByDescending(audit => audit.Id)
            .Select(audit => audit.Detail)
            .FirstAsync(TestContext.Current.CancellationToken));
        Assert.Contains("timeZone: ", detail, StringComparison.Ordinal);
        Assert.Contains("to 'W. Europe Standard Time'", detail, StringComparison.Ordinal);
    }

    // Saving what is stored changes nothing and records nothing.
    [Fact]
    public async Task ASaveOfWhatIsStoredChangesNothing()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SettingsSectionView<LoggingSettings> before = await administrator.SectionAsync<LoggingSettings>(SettingsSectionNames.Logging);

        SettingsSectionView<LoggingSettings> after = await RegisteredMachine.ReadAsync<SettingsSectionView<LoggingSettings>>(
            await administrator.SaveAsync(SettingsSectionNames.Logging, before.Version, before.Values));

        Assert.Equal(before.Version, after.Version);
    }

    [Fact]
    public async Task ASaveOverAnotherIsAConflict()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SettingsSectionView<ProxySettings> loaded = await administrator.SectionAsync<ProxySettings>(SettingsSectionNames.Proxies);
        await administrator.SavedAsync<LoggingSettings>(SettingsSectionNames.Logging, values => values);

        HttpResponseMessage stale = await administrator.SaveAsync(SettingsSectionNames.Proxies, loaded.Version - 1, loaded.Values);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("Someone saved proxies since you loaded it. Load it again.", await TestDatabase.TitleAsync(stale));
    }

    [Fact]
    public async Task ProblemsAreKeyedByFieldAndNothingIsSaved()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SettingsSectionView<MachineSettings> before = await administrator.SectionAsync<MachineSettings>(SettingsSectionNames.Machines);

        HttpResponseMessage response = await administrator.SaveAsync(
            SettingsSectionNames.Machines,
            before.Version,
            before.Values with { MaxWaiting = 10, MaxWaitingPerAddress = 50 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        HttpValidationProblemDetails problems = await SettingsRequests.ProblemsAsync(response);
        Assert.Equal(["Must be between 1 and MaxWaiting, which is 10."], problems.Errors["maxWaitingPerAddress"]);
        Assert.Equal(before.Version, (await administrator.SectionAsync<MachineSettings>(SettingsSectionNames.Machines)).Version);
    }

    // Only whether a secret is set ever comes back, and the audit names the field, never the value.
    [Fact]
    public async Task SecretsAreSetKeptAndClearedButNeverSent()
    {
        const string Password = "Local administrator 7";
        SignedInClient administrator = await application.AdministratorAsync();
        SettingsSectionView<DeploymentSettings> loaded = await administrator.SectionAsync<DeploymentSettings>(SettingsSectionNames.Deployment);

        HttpResponseMessage set = await administrator.SaveAsync(
            SettingsSectionNames.Deployment,
            loaded.Version,
            loaded.Values,
            new Dictionary<string, SecretUpdate> { ["localAdministrator.password"] = new(SecretAction.Set, Password) });
        string body = await set.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        SettingsSectionView<DeploymentSettings> withSecret = await RegisteredMachine.ReadAsync<SettingsSectionView<DeploymentSettings>>(set);

        Assert.DoesNotContain(Password, body, StringComparison.Ordinal);
        Assert.True(withSecret.Secrets["localAdministrator.password"].IsSet);
        Assert.False(withSecret.Secrets["domain.password"].IsSet);
        Assert.Equal(Password, application.Services.GetRequiredService<DdtSettings>().Current.Deployment.LocalAdministrator.Password);

        SettingsSectionView<DeploymentSettings> kept = await administrator.SavedAsync<DeploymentSettings>(
            SettingsSectionNames.Deployment,
            values => values with { Locale = "de-DE" });
        Assert.True(kept.Secrets["localAdministrator.password"].IsSet);

        SettingsSectionView<DeploymentSettings> cleared = await administrator.SavedAsync<DeploymentSettings>(
            SettingsSectionNames.Deployment,
            values => values,
            new Dictionary<string, SecretUpdate> { ["localAdministrator.password"] = new(SecretAction.Clear, null) });
        Assert.False(cleared.Secrets["localAdministrator.password"].IsSet);

        List<string?> details = await application.QueryAsync(database => database.AuditEvents
            .Where(audit => audit.SubjectId == SettingsSectionNames.Deployment)
            .Select(audit => audit.Detail)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Contains(details, detail => detail!.Contains("localAdministrator.password set", StringComparison.Ordinal));
        Assert.Contains(details, detail => detail!.Contains("localAdministrator.password cleared", StringComparison.Ordinal));
        Assert.DoesNotContain(details, detail => detail!.Contains(Password, StringComparison.Ordinal));
        Assert.DoesNotContain(
            await application.QueryAsync(database => database.SettingsSections.Select(row => row.Values + row.Secrets).ToListAsync(TestContext.Current.CancellationToken)),
            stored => stored.Contains(Password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASecretTheSectionDoesNotHaveIsRefused()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SettingsSectionView<DeploymentSettings> loaded = await administrator.SectionAsync<DeploymentSettings>(SettingsSectionNames.Deployment);

        HttpResponseMessage response = await administrator.SaveAsync(
            SettingsSectionNames.Deployment,
            loaded.Version,
            loaded.Values,
            new Dictionary<string, SecretUpdate> { ["timeZone"] = new(SecretAction.Set, "x") });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("timeZone", (await SettingsRequests.ProblemsAsync(response)).Errors.Keys);
    }

    [Fact]
    public async Task OperatorsReadTheDeploymentAndMachinesSectionsAndNothingElse()
    {
        SignedInClient @operator = await application.SignInAsync(DdtRoleNames.Operator);
        SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);

        Assert.Equal(HttpStatusCode.OK, (await @operator.GetAsync("/api/settings/deployment")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await @operator.GetAsync("/api/settings/machines")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await @operator.GetAsync("/api/settings/ldap")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await @operator.GetAsync("/api/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await @operator.SaveAsync(SettingsSectionNames.Machines, 0, new MachineSettings(true, 1, 1, []))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/settings/deployment")).StatusCode);
    }

    [Fact]
    public async Task TheOverviewListsEverySectionAndOnlyAllowedServerValues()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        SettingsOverview overview = await RegisteredMachine.ReadAsync<SettingsOverview>(await administrator.GetAsync("/api/settings"));

        Assert.Equal(
            ["deployment", "machines", "ldap", "oidc", "proxies", "pxe", "logging", "certificate"],
            overview.Sections.Select(section => section.Section));
        Assert.Equal(SettingsSectionKind.Restart, overview.Sections.Single(section => section.Section == "pxe").Kind);
        Assert.True(overview.KeyRingReadable);
        Assert.Equal("web", overview.Server.Single(setting => setting.Key == "DDT:Roles").Value);
        Assert.Equal(application.StorePath, overview.Server.Single(setting => setting.Key == "DDT:StorePath").Value);
        ServerSetting connection = overview.Server.Single(setting => setting.Key == "ConnectionStrings:ddtdb");
        Assert.True(connection.Secret);
        Assert.StartsWith("SQLite", connection.Value, StringComparison.Ordinal);
        Assert.Null(overview.Server.Single(setting => setting.Key == "DDT:Https:SubjectAlternativeNames").Value);
    }

    // A zero touch network grants trust, so it needs a fresh proof of identity, and the answer names the field.
    [Fact]
    public async Task AFieldThatGrantsTrustNeedsAFreshProofOfIdentity()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SettingsSectionView<MachineSettings> loaded = await administrator.SectionAsync<MachineSettings>(SettingsSectionNames.Machines);
        MachineSettings values = loaded.Values with { ZeroTouchNetworks = ["10.210.0.0/16"] };

        HttpResponseMessage refused = await administrator.SaveAsync(SettingsSectionNames.Machines, loaded.Version, values);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("zeroTouchNetworks", await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Contains("zeroTouchNetworks", loaded.Reauthenticate);

        SettingsSectionView<MachineSettings> saved = await RegisteredMachine.ReadAsync<SettingsSectionView<MachineSettings>>(
            await administrator.SaveAsync(SettingsSectionNames.Machines, loaded.Version, values, reauthentication: await administrator.TokenAsync()));

        Assert.Equal(["10.210.0.0/16"], saved.Values.ZeroTouchNetworks);

        // What is stored already is no change, and needs no proof.
        Assert.Equal(HttpStatusCode.OK, (await administrator.SaveAsync(SettingsSectionNames.Machines, saved.Version, saved.Values)).StatusCode);
    }

    [Fact]
    public async Task AWideNetworkIsSavedOnlyOnceConfirmed()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string token = await administrator.TokenAsync();
        SettingsSectionView<ProxySettings> loaded = await administrator.SectionAsync<ProxySettings>(SettingsSectionNames.Proxies);
        ProxySettings values = loaded.Values with { KnownNetworks = ["172.16.0.0/12"] };

        HttpResponseMessage unconfirmed = await administrator.SaveAsync(SettingsSectionNames.Proxies, loaded.Version, values, reauthentication: token);

        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        Assert.Equal([SettingWarningCodes.WideNetwork], await SettingsRequests.UnconfirmedAsync(unconfirmed));
        Assert.StartsWith("network.wide: 172.16.0.0/12 is wider than a /16.", Assert.Single((await SettingsRequests.ProblemsAsync(unconfirmed)).Errors["confirm"]), StringComparison.Ordinal);

        SettingsSectionView<ProxySettings> saved = await RegisteredMachine.ReadAsync<SettingsSectionView<ProxySettings>>(
            await administrator.SaveAsync(SettingsSectionNames.Proxies, loaded.Version, values, confirm: [SettingWarningCodes.WideNetwork], reauthentication: token));

        Assert.Equal(SettingWarningCodes.WideNetwork, Assert.Single(saved.Warnings).Code);
        Assert.NotNull(saved.Apply);

        // A warning that holds already is not asked again.
        Assert.Equal(HttpStatusCode.OK, (await administrator.SaveAsync(SettingsSectionNames.Proxies, saved.Version, saved.Values)).StatusCode);

        await administrator.SavedAsync<ProxySettings>(SettingsSectionNames.Proxies, current => current with { KnownNetworks = [] }, reauthentication: token);
    }

    // The overlap belongs to the machines section at load, and a save of either section is refused.
    [Fact]
    public async Task AProxyInsideAZeroTouchNetworkIsRefusedOnASaveOfEitherSection()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string token = await administrator.TokenAsync();
        await administrator.SavedAsync<MachineSettings>(
            SettingsSectionNames.Machines,
            values => values with { ZeroTouchNetworks = ["10.220.0.0/16"] },
            reauthentication: token);
        SettingsSectionView<ProxySettings> proxies = await administrator.SectionAsync<ProxySettings>(SettingsSectionNames.Proxies);

        HttpResponseMessage refused = await administrator.SaveAsync(
            SettingsSectionNames.Proxies,
            proxies.Version,
            proxies.Values with { KnownProxies = ["10.220.0.5"] },
            reauthentication: token);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.StartsWith(
            "The zero touch network 10.220.0.0/16 contains the proxy 10.220.0.5.",
            Assert.Single((await SettingsRequests.ProblemsAsync(refused)).Errors["knownProxies"]),
            StringComparison.Ordinal);

        await administrator.SavedAsync<MachineSettings>(SettingsSectionNames.Machines, values => values with { ZeroTouchNetworks = [] }, reauthentication: token);
    }

    // A live setting applies to the next decision, with no restart.
    [Fact]
    public async Task ASavedMachinesSectionAppliesAtOnce()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        await administrator.SavedAsync<MachineSettings>(SettingsSectionNames.Machines, values => values with { RequireWebApproval = true });
        Assert.True((await RegisteredMachine.ReadAsync<DeploymentOptionsView>(await administrator.GetAsync("/api/deployments/options"))).RequireWebApproval);

        await administrator.SavedAsync<MachineSettings>(SettingsSectionNames.Machines, values => values with { RequireWebApproval = false });
        Assert.False((await RegisteredMachine.ReadAsync<DeploymentOptionsView>(await administrator.GetAsync("/api/deployments/options"))).RequireWebApproval);
    }

    [Fact]
    public async Task AnUnconfirmedLdapRekeyIsRefused()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SettingsSectionView<LdapSettings> loaded = await administrator.SectionAsync<LdapSettings>(SettingsSectionNames.Ldap);

        HttpResponseMessage response = await administrator.SaveAsync(
            SettingsSectionNames.Ldap,
            loaded.Version,
            loaded.Values with { ImmutableIdAttribute = "entryUUID" });

        // No local administrator is missing here, so the re-key is the one warning to confirm.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([SettingWarningCodes.LdapRekey], await SettingsRequests.UnconfirmedAsync(response));
    }
}
