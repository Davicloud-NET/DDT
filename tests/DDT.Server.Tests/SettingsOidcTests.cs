// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Text;
using DDT.Contracts.Authentication;
using DDT.Contracts.Settings;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace DDT.Server.Tests;

// Single sign-on is switched on and off while the server runs: the scheme is added and removed at runtime, its options
// come from the snapshot, and a scheme that cannot start is removed while local sign-in keeps working.
public sealed class SettingsOidcTests(SettingsOidcTests.OidcApplication application) : IClassFixture<SettingsOidcTests.OidcApplication>
{
    private const string Authority = "https://idp.example/realms/ddt";

    [Fact]
    public async Task TurningSingleSignOnOnAndOffAddsAndRemovesTheScheme()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        IAuthenticationSchemeProvider schemes = application.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        application.BreakHandler = false;

        Assert.Null(await schemes.GetSchemeAsync(OidcOptions.SchemeName));
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.GetAsync("/api/auth/external/start")).StatusCode);

        SettingsSectionView<OidcSettings> on = await SaveAsync(administrator, enabled: true);

        AuthenticationScheme? scheme = await schemes.GetSchemeAsync(OidcOptions.SchemeName);
        Assert.Equal(typeof(OpenIdConnectHandler), scheme?.HandlerType);
        Assert.Equal("Contoso", scheme?.DisplayName);
        SettingApplyState applied = Assert.Single(on.Apply!);
        Assert.Equal(SettingApplyStatus.Applied, applied.State);
        Assert.Equal(on.Version, applied.Version);

        OpenIdConnectOptions options = application.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OidcOptions.SchemeName);
        Assert.Equal(Authority, options.Authority);
        Assert.Equal("ddt-client", options.ClientId);
        Assert.Equal("client secret 1", options.ClientSecret);
        Assert.Equal(OidcSchemeOptions.CallbackPath, options.CallbackPath);
        Assert.Equal(
            [new ExternalProvider(OidcOptions.SchemeName, "Contoso")],
            await RegisteredMachine.ReadAsync<List<ExternalProvider>>(await administrator.GetAsync("/api/auth/external/providers")));

        await SaveAsync(administrator, enabled: false);

        Assert.Null(await schemes.GetSchemeAsync(OidcOptions.SchemeName));
        Assert.Empty(await RegisteredMachine.ReadAsync<List<ExternalProvider>>(await administrator.GetAsync("/api/auth/external/providers")));
    }

    // The authentication middleware builds the options of every registered remote scheme on every request, so a scheme
    // that cannot start would take every request down with it. It is removed instead, and the host says why.
    [Fact]
    public async Task ASchemeThatCannotStartIsRemovedAndLocalSignInKeepsWorking()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        application.BreakHandler = true;

        try
        {
            SettingsSectionView<OidcSettings> failed = await SaveAsync(administrator, enabled: true, clientId: "broken-client");

            SettingApplyState state = Assert.Single(failed.Apply!);
            Assert.Equal(SettingApplyStatus.Failed, state.State);
            Assert.Contains("The handler does not start in this test.", state.Message, StringComparison.Ordinal);
            Assert.Null(await application.Services.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync(OidcOptions.SchemeName));

            CookieContainer cookies = new();
            using SignedInClient browser = new(application.CreateDefaultClient(new CookieContainerHandler(cookies)), cookies);
            HttpResponseMessage login = await browser.PostAsync("/api/auth/login", new LoginRequest("nobody", "wrong password", null, null));
            Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

            await SettingsPxeTests.Eventually(() => application.QueryAsync(database => database.AuditEvents.AnyAsync(
                audit => audit.Action == DDT.Server.Machines.AuditActions.SettingsApplyFailed && audit.SubjectId == SettingsSectionNames.Oidc,
                TestContext.Current.CancellationToken)));
        }
        finally
        {
            application.BreakHandler = false;
            await SaveAsync(administrator, enabled: false);
        }
    }

    // A stored client secret goes only to the provider it was entered for.
    [Fact]
    public async Task AStoredClientSecretIsNotKeptForAnotherProvider()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        application.BreakHandler = false;
        await SaveAsync(administrator, enabled: false);
        SettingsSectionView<OidcSettings> loaded = await administrator.SectionAsync<OidcSettings>(SettingsSectionNames.Oidc);

        HttpResponseMessage refused = await administrator.SaveAsync(
            SettingsSectionNames.Oidc,
            loaded.Version,
            loaded.Values with { Authority = "https://attacker.example" },
            reauthentication: await administrator.TokenAsync());

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(
            ["Enter it again for the new server: a stored secret goes only to the server it was entered for."],
            (await SettingsRequests.ProblemsAsync(refused)).Errors["clientSecret"]);
        Assert.True(await application.QueryAsync(database => database.AuditEvents.AnyAsync(
            audit => audit.Action == DDT.Server.Machines.AuditActions.SettingsRefused && audit.SubjectId == SettingsSectionNames.Oidc,
            TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task TheTestReadsTheProvidersDiscoveryDocument()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        OidcTestResult reached = await RegisteredMachine.ReadAsync<OidcTestResult>(
            await administrator.PostAsync("/api/settings/oidc/test", new OidcTestRequest(Authority)));
        OidcTestResult elsewhere = await RegisteredMachine.ReadAsync<OidcTestResult>(
            await administrator.PostAsync("/api/settings/oidc/test", new OidcTestRequest("https://idp.example/other")));
        HttpResponseMessage plain = await administrator.PostAsync("/api/settings/oidc/test", new OidcTestRequest("http://idp.example"));

        Assert.True(reached.Reached);
        Assert.Equal(Authority, reached.Issuer);
        Assert.Equal("http://localhost/api/auth/external/callback", reached.RedirectUri);
        Assert.StartsWith($"The provider answered as {Authority}.", reached.Message, StringComparison.Ordinal);
        Assert.False(elsewhere.Reached);
        Assert.StartsWith("https://idp.example/other/.well-known/openid-configuration answered 404", elsewhere.Message, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, plain.StatusCode);
    }

    private static async Task<SettingsSectionView<OidcSettings>> SaveAsync(SignedInClient administrator, bool enabled, string clientId = "ddt-client")
    {
        SettingsSectionView<OidcSettings> loaded = await administrator.SectionAsync<OidcSettings>(SettingsSectionNames.Oidc);
        HttpResponseMessage response = await administrator.SaveAsync(
            SettingsSectionNames.Oidc,
            loaded.Version,
            loaded.Values with { Enabled = enabled, Authority = Authority, ClientId = clientId, DisplayName = "Contoso" },
            new Dictionary<string, SecretUpdate> { ["clientSecret"] = new(SecretAction.Set, "client secret 1") },
            reauthentication: await administrator.TokenAsync());

        return await RegisteredMachine.ReadAsync<SettingsSectionView<OidcSettings>>(response);
    }

    // A provider that answers the discovery request for the authority, and a switch that makes the handler's options fail
    // after the save's own checks, as a provider could make them fail only once it is running.
    public sealed class OidcApplication : DdtApplication
    {
        public bool BreakHandler { get; set; }

        protected override void ConfigureTestHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient(SettingsServiceCollectionExtensions.OidcTestClient)
                    .ConfigurePrimaryHttpMessageHandler(() => new DiscoveryHandler());
                services.AddSingleton<IPostConfigureOptions<OpenIdConnectOptions>>(new BreakingPostConfigure(this));
            });
        }

        private sealed class BreakingPostConfigure(OidcApplication application) : IPostConfigureOptions<OpenIdConnectOptions>
        {
            public void PostConfigure(string? name, OpenIdConnectOptions options)
            {
                if (application.BreakHandler && options.ClientId == "broken-client")
                {
                    throw new InvalidOperationException("The handler does not start in this test.");
                }
            }
        }
    }

    private sealed class DiscoveryHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri?.AbsoluteUri == $"{Authority}/.well-known/openid-configuration"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""{"issuer":"{{Authority}}","authorization_endpoint":"{{Authority}}/auth"}""", Encoding.UTF8, "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
