// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Configuration;
using DDT.Server.Data;
using DDT.Server.Ldap;
using DDT.Server.Machines;
using DDT.Server.Tokens;
using DDT.Server.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        services.AddScoped<ILdapAuthenticator, LdapAuthenticator>();
        services.AddScoped<DirectorySignInService>();
        services.AddScoped<CredentialVerifier>();
        services.AddScoped<ExternalSignIn>();
        services.AddScoped<UserViews>();
        services.AddScoped<UserActivity>();
        services.AddScoped<UserChangePublisher>();
        services.AddScoped<UserAccounts>();
        services.AddSingleton<LastAdministratorGuard>();
        services.AddScoped<ApiTokens>();

        // The key ring can mint an administrator cookie and every machine token, so it has to survive restarts and live
        // on the store volume, not in the read-only layer.
        services.AddDataProtection()
            .SetApplicationName("ddt")
            .PersistKeysToFileSystem(Directory.CreateDirectory(Path.Combine(options.StorePath, "keys")));

        AddIdentity(services);
        services.AddSingleton<MachineTokenService>();
        AddSchemes(services);
        ConfigureCookie(services, options);

        // The default is 30 minutes, which is how long a disabled account keeps working.
        services.Configure<SecurityStampValidatorOptions>(stamp => stamp.ValidationInterval = TimeSpan.FromMinutes(1));

        services.AddSingleton<FirstAdministratorFile>();
        services.AddHostedService<IdentityBootstrap>();

        return services;
    }

    private static void AddIdentity(IServiceCollection services)
    {
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
            .AddErrorDescriber<DdtIdentityErrorDescriber>()
            .AddEntityFrameworkStores<DdtDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // OWASP recommends 210,000 iterations for PBKDF2-HMAC-SHA512, and the framework uses 100,000. Identity rehashes
        // the password at the next successful sign-in.
        services.Configure<PasswordHasherOptions>(hasher => hasher.IterationCount = 210_000);
    }

    private static void AddSchemes(IServiceCollection services)
    {
        // Call AddIdentityCookies before the cookie is configured, never a bare AddCookie for the scheme. A bare
        // AddCookie silently drops SecurityStampValidator, and that's what makes disabling an account take effect.
        AuthenticationBuilder authentication = services.AddAuthentication(IdentityConstants.ApplicationScheme);
        authentication.AddIdentityCookies();
        authentication.AddScheme<AuthenticationSchemeOptions, MachineAuthenticationHandler>(
            DdtAuthenticationSchemes.Machine,
            configureOptions: null);
        authentication.AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(
            DdtAuthenticationSchemes.ApiToken,
            configureOptions: null);

        // Any bearer token goes to the API token scheme, which ignores machine tokens. So a request with a bearer token
        // is never treated as the browser session, even if it also carries the session cookie.
        authentication.AddPolicyScheme(DdtAuthenticationSchemes.User, displayName: null, user =>
            user.ForwardDefaultSelector = context =>
                context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? DdtAuthenticationSchemes.ApiToken
                    : IdentityConstants.ApplicationScheme);
    }

    private static void ConfigureCookie(IServiceCollection services, DdtOptions options) =>
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, cookie =>
        {
            cookie.Cookie.Name = options.RequireHttps ? "__Host-ddt-auth" : "ddt-auth";
            cookie.Cookie.HttpOnly = true;

            // Lax even while single sign-on is off, because cookie options only change at a restart. Strict would drop
            // the session that starts with the provider's redirect back. The same-origin and antiforgery filters refuse
            // cross-site changes.
            cookie.Cookie.SameSite = SameSiteMode.Lax;
            cookie.Cookie.SecurePolicy = options.RequireHttps
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;
            cookie.Cookie.Path = "/";
            cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
            cookie.SlidingExpiration = true;
        });
}
