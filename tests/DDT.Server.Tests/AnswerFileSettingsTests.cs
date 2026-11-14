// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Settings;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// The product key, the organization and the owner on the Deployment defaults page.
public sealed class AnswerFileSettingsTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private const string Key = "NPPR9-FWDCX-D2C8J-H872K-2YT43";

    [Fact]
    public async Task TheOrganizationAndOwnerAreSavedAndShown()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        SettingsSectionView<DeploymentSettings> saved = await administrator.SavedAsync<DeploymentSettings>(
            SettingsSectionNames.Deployment,
            values => values with { Organization = " Davicloud ", Owner = "IT" });

        Assert.Equal(("Davicloud", "IT"), (saved.Values.Organization, saved.Values.Owner));
        Assert.Equal("Davicloud", application.Services.GetRequiredService<DdtSettings>().Current.Deployment.Organization);
    }

    [Fact]
    public async Task TheProductKeyIsWriteOnly()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SettingsSectionView<DeploymentSettings> loaded = await administrator.SectionAsync<DeploymentSettings>(SettingsSectionNames.Deployment);

        HttpResponseMessage set = await administrator.SaveAsync(
            SettingsSectionNames.Deployment,
            loaded.Version,
            loaded.Values,
            new(Secrets: new Dictionary<string, SecretUpdate> { ["productKey"] = new(SecretAction.Set, Key) }));
        string body = await set.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        SettingsSectionView<DeploymentSettings> view = await RegisteredMachine.ReadAsync<SettingsSectionView<DeploymentSettings>>(set);

        Assert.True(view.Secrets["productKey"].IsSet);
        Assert.DoesNotContain(Key, body, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, await (await administrator.GetAsync("/api/settings/deployment")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(Key, application.Services.GetRequiredService<DdtSettings>().Current.Deployment.ProductKey);
        Assert.DoesNotContain(
            await application.QueryAsync(database => database.AuditEvents.Select(audit => audit.Detail ?? "").ToListAsync(TestContext.Current.CancellationToken)),
            detail => detail.Contains(Key, StringComparison.Ordinal));
        Assert.DoesNotContain(
            await application.QueryAsync(database => database.SettingsSections.Select(row => row.Values + row.Secrets).ToListAsync(TestContext.Current.CancellationToken)),
            stored => stored.Contains(Key, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AKeyThatIsNoKeyIsRefusedWithoutBeingQuoted()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SettingsSectionView<DeploymentSettings> loaded = await administrator.SectionAsync<DeploymentSettings>(SettingsSectionNames.Deployment);

        HttpResponseMessage refused = await administrator.SaveAsync(
            SettingsSectionNames.Deployment,
            loaded.Version,
            loaded.Values,
            new(Secrets: new Dictionary<string, SecretUpdate> { ["productKey"] = new(SecretAction.Set, "SECRET-12345") }));

        Assert.False(refused.IsSuccessStatusCode);
        Assert.DoesNotContain("SECRET", await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }
}
