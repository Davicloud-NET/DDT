// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Settings;
using DDT.Server.Authentication;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DDT.Server.Tests.Autopilot.AutopilotLab;

namespace DDT.Server.Tests.Autopilot;

// The Autopilot section of the settings page: its secret is never read back, and it never goes to another app.
public sealed class AutopilotSettingsTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // An earlier test may have stored one, which these saves would otherwise have to keep.
    private static SettingsSaveExtras WithoutSecret =>
        new(Secrets: new Dictionary<string, SecretUpdate> { ["clientSecret"] = new(SecretAction.Clear, null) });

    [Fact]
    public async Task TheSecretIsWrittenButNeverReadBack()
    {
        SettingsSectionView<AutopilotSettings> saved = await SetUpAsync(application, "Default");

        Assert.Equal(new AutopilotSettings(TenantId, ClientId, "Default"), saved.Values);
        Assert.True(saved.Secrets["clientSecret"].IsSet);
        SignedInClient administrator = await application.AdministratorAsync();
        string json = await (await administrator.GetAsync($"/api/settings/{SettingsSectionNames.Autopilot}")).Content.ReadAsStringAsync(Cancellation);
        Assert.DoesNotContain(Secret, json, StringComparison.Ordinal);
        Assert.True(application.Services.GetRequiredService<DdtSettings>().Current.Autopilot.CanUpload());
    }

    // As with single sign-on, a stored secret must be entered again before it goes to another tenant or app.
    [Fact]
    public async Task ANewTenantOrClientNeedsTheSecretAgain()
    {
        await SetUpAsync(application);
        SignedInClient administrator = await application.AdministratorAsync();
        SettingsSectionView<AutopilotSettings> loaded = await administrator.SectionAsync<AutopilotSettings>(SettingsSectionNames.Autopilot);

        HttpResponseMessage moved = await administrator.SaveAsync(
            SettingsSectionNames.Autopilot,
            loaded.Version,
            loaded.Values with { TenantId = "fabrikam.onmicrosoft.com" });

        Assert.Equal(HttpStatusCode.BadRequest, moved.StatusCode);
        HttpValidationProblemDetails problems = await SettingsRequests.ProblemsAsync(moved);
        Assert.Contains("clientSecret", problems.Errors.Keys);
    }

    [Fact]
    public async Task RefusesWhatCannotBeATenantAnAppOrAGroupTag()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SettingsSectionView<AutopilotSettings> loaded = await administrator.SectionAsync<AutopilotSettings>(SettingsSectionNames.Autopilot);

        HttpResponseMessage response = await administrator.SaveAsync(
            SettingsSectionNames.Autopilot,
            loaded.Version,
            new AutopilotSettings("contoso/../x", "ddt-client", "Sales,Vienna"),
            WithoutSecret);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        HttpValidationProblemDetails problems = await SettingsRequests.ProblemsAsync(response);
        Assert.Equal(["clientId", "defaultGroupTag", "tenantId"], problems.Errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task NeedsTheTenantAndTheClientTogether()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SettingsSectionView<AutopilotSettings> loaded = await administrator.SectionAsync<AutopilotSettings>(SettingsSectionNames.Autopilot);

        HttpResponseMessage response = await administrator.SaveAsync(
            SettingsSectionNames.Autopilot,
            loaded.Version,
            new AutopilotSettings(Guid.NewGuid().ToString(), string.Empty, string.Empty),
            WithoutSecret);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["clientId"], (await SettingsRequests.ProblemsAsync(response)).Errors.Keys);
    }

    [Theory]
    [InlineData(DdtRoleNames.Operator)]
    [InlineData(DdtRoleNames.Viewer)]
    public async Task OnlyAnAdministratorReadsTheSection(string role)
    {
        using SignedInClient client = await application.SignInAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/settings/{SettingsSectionNames.Autopilot}")).StatusCode);
    }
}
