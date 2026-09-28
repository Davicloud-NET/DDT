// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Authentication;
using DDT.Server.Ldap;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DDT.Server.Settings;

public static class SettingsServiceCollectionExtensions
{
    public const string OidcTestClient = "ddt.oidc-test";

    // After the data services, so the settings service starts once the schema exists, and before every hosted service
    // that reads the settings as it starts.
    public static IServiceCollection AddDdtSettings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<DdtSettings>();
        services.AddSingleton<SettingsProtector>();
        services.AddSingleton<ReauthenticationTokens>();
        services.AddSingleton<DirectoryProofs>();
        services.AddSingleton<SettingsHostStates>();
        services.AddSingleton<SettingsApplier>();
        services.AddScoped<SettingsSaveChecks>();
        services.AddSingleton<SettingsSecretChanges>();
        services.AddScoped<SettingsStore>();
        services.AddScoped<SettingsKeyRing>();
        services.AddScoped<SettingsImporter>();
        services.AddScoped<SettingsViews>();
        services.AddScoped<SettingsSaves>();
        services.AddScoped<LdapSettingsTest>();
        services.AddScoped<OidcSettingsTest>();
        services.AddScoped<ReleaseUploads>();
        services.AddScoped<ConsoleLogos>();
        services.AddScoped<CertificateChanges>();
        services.TryAddSingleton<ILdapTester, LdapTester>();
        services.AddHttpClient(OidcTestClient, client => client.Timeout = TimeSpan.FromSeconds(10));

        // The handler's services always exist; the scheme itself is added and removed as the oidc section changes.
        services.AddTransient<OpenIdConnectHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<OpenIdConnectOptions>, OpenIdConnectPostConfigureOptions>());
        services.AddSingleton<OidcSchemeBridge>();
        services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>>(provider => provider.GetRequiredService<OidcSchemeBridge>());
        services.AddSingleton<IOptionsChangeTokenSource<OpenIdConnectOptions>>(provider => provider.GetRequiredService<OidcSchemeBridge>());
        // The options monitor builds the options again as soon as the settings change, whether or not the scheme is
        // registered, so they are validated only while single sign-on is on.
        services.AddOptions<OpenIdConnectOptions>(OidcOptions.SchemeName).Validate<DdtSettings>((options, settings) =>
        {
            if (settings.Current.Oidc.Enabled)
            {
                options.Validate(OidcOptions.SchemeName);
            }

            return true;
        });

        // After the host's own logging configuration, so these rules come later and win for the same category.
        services.AddSingleton<LoggingSettingsBridge>();
        services.AddSingleton<IConfigureOptions<LoggerFilterOptions>>(provider => provider.GetRequiredService<LoggingSettingsBridge>());
        services.AddSingleton<IOptionsChangeTokenSource<LoggerFilterOptions>>(provider => provider.GetRequiredService<LoggingSettingsBridge>());

        services.AddSingleton<SettingsService>();
        services.AddHostedService(provider => provider.GetRequiredService<SettingsService>());

        // After the settings service, which registers the scheme it checks.
        services.AddHostedService<ExternalSignInSchemeGuard>();
        services.AddHostedService<CertificateRollbackRecorder>();

        return services;
    }
}
