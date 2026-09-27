// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using DDT.Core.Configuration;
using DDT.Server.Configuration;
using DDT.Server.Data;
using DDT.Server.Ldap;
using DDT.Server.Machines;
using DDT.Server.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace DDT.Server.Authentication;

public static class DdtAuthenticationExtensions
{
    public static IServiceCollection AddDdtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        DdtOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        OidcOptions oidc = configuration.GetSection(OidcOptions.SectionName).Get<OidcOptions>() ?? new OidcOptions();
        SettingProblem.ThrowIfAny(OidcOptions.SectionName, OidcOptionsValidation.FindProblems(oidc));
        services.Configure<OidcOptions>(configuration.GetSection(OidcOptions.SectionName));
        services.Configure<LdapOptions>(configuration.GetSection(LdapOptions.SectionName));
        services.AddScoped<ILdapAuthenticator, LdapAuthenticator>();
        services.AddScoped<DirectorySignInService>();
        services.AddScoped<CredentialVerifier>();
        services.AddScoped<UserViews>();
        services.AddScoped<UserActivity>();

        // The key ring can mint an administrator cookie and every machine token, so it has to
        // survive restarts and it has to live on the store volume, not in the read only layer.
        services.AddDataProtection()
            .SetApplicationName("ddt")
            .PersistKeysToFileSystem(Directory.CreateDirectory(Path.Combine(options.StorePath, "keys")));

        services.AddIdentityCore<DdtUser>(identity =>
            {
                identity.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
                identity.User.RequireUniqueEmail = false;
                identity.Password.RequiredLength = 12;
                identity.Password.RequireNonAlphanumeric = false;
                identity.Lockout.AllowedForNewUsers = true;
                identity.Lockout.MaxFailedAccessAttempts = 5;
                identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                identity.Tokens.AuthenticatorIssuer = "DDT";
                identity.SignIn.RequireConfirmedAccount = false;
            })
            .AddRoles<DdtRole>()
            .AddEntityFrameworkStores<DdtDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // The framework default is 100,000. OWASP currently recommends 210,000 for
        // PBKDF2-HMAC-SHA512, and Identity rehashes on the next successful sign in.
        services.Configure<PasswordHasherOptions>(hasher => hasher.IterationCount = 210_000);

        // AddIdentityCookies first, then Configure. A bare AddCookie for the same scheme silently
        // drops SecurityStampValidator, which is what makes disabling an account take effect.
        services.AddSingleton<MachineTokenService>();

        AuthenticationBuilder authentication = services.AddAuthentication(IdentityConstants.ApplicationScheme);
        authentication.AddIdentityCookies();
        authentication.AddScheme<AuthenticationSchemeOptions, MachineAuthenticationHandler>(
            DdtAuthenticationSchemes.Machine,
            configureOptions: null);

        services.Configure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, cookie =>
        {
            cookie.Cookie.Name = options.RequireHttps ? "__Host-ddt-auth" : "ddt-auth";
            cookie.Cookie.HttpOnly = true;
            // Lax whether single sign-on is on or not, as the maintainer decided: the provider sends the browser
            // back with a navigation from its own site, on which Strict would leave the new session behind, and a
            // value that followed Oidc:Enabled could not change without a restart. Cross site requests that change
            // something are refused by the same origin and antiforgery filters, which do not rely on SameSite.
            cookie.Cookie.SameSite = SameSiteMode.Lax;
            cookie.Cookie.SecurePolicy = options.RequireHttps
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;
            cookie.Cookie.Path = "/";
            cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
            cookie.SlidingExpiration = true;
        });

        if (oidc.Enabled)
        {
            AddOpenIdConnect(authentication, oidc);
            services.AddHostedService<ExternalSignInSchemeGuard>();
        }

        // The default is 30 minutes, which is how long a disabled account keeps working.
        services.Configure<SecurityStampValidatorOptions>(stamp => stamp.ValidationInterval = TimeSpan.FromMinutes(1));

        services.AddHostedService<IdentityBootstrap>();

        return services;
    }

    private static void AddOpenIdConnect(AuthenticationBuilder authentication, OidcOptions oidc)
    {
        authentication.AddOpenIdConnect(OidcOptions.SchemeName, oidc.DisplayName, openId =>
        {
            // Without this the external principal is signed straight into the application cookie:
            // no local user, no link row, no roles, no lockout and no second factor.
            openId.SignInScheme = IdentityConstants.ExternalScheme;

            openId.Authority = oidc.Authority;
            openId.ClientId = oidc.ClientId;
            openId.ClientSecret = oidc.ClientSecret;

            // The handler defaults to the implicit flow, which leaves PKCE inert.
            openId.ResponseType = OpenIdConnectResponseType.Code;
            openId.ResponseMode = OpenIdConnectResponseMode.Query;
            openId.UsePkce = true;

            openId.GetClaimsFromUserInfoEndpoint = true;
            openId.SaveTokens = false;
            openId.CallbackPath = "/api/auth/external/callback";

            openId.Scope.Clear();

            foreach (string scope in oidc.Scopes)
            {
                openId.Scope.Add(scope);
            }

            // The groups claim is read at the sign-in, from the options of that moment.
            openId.Events.OnUserInformationReceived = context =>
            {
                if (context.Principal?.Identity is ClaimsIdentity identity)
                {
                    SingleSignOnGroups.CopyFromUserInformation(
                        context.User.RootElement,
                        identity,
                        context.HttpContext.RequestServices.GetRequiredService<IOptions<OidcOptions>>().Value.GroupsClaim,
                        context.Options.ClaimsIssuer ?? OidcOptions.SchemeName);
                }

                return Task.CompletedTask;
            };
        });
    }
}
