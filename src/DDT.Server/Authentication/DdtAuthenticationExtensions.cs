using DDT.Server.Configuration;
using DDT.Server.Data;
using DDT.Server.Ldap;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

        services.Configure<LdapOptions>(configuration.GetSection(LdapOptions.SectionName));
        services.AddScoped<ILdapAuthenticator, LdapAuthenticator>();
        services.AddScoped<DirectorySignInService>();

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
            cookie.Cookie.SameSite = SameSiteMode.Strict;
            cookie.Cookie.SecurePolicy = options.RequireHttps
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;
            cookie.Cookie.Path = "/";
            cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
            cookie.SlidingExpiration = true;
        });

        // The default is 30 minutes, which is how long a disabled account keeps working.
        services.Configure<SecurityStampValidatorOptions>(stamp => stamp.ValidationInterval = TimeSpan.FromMinutes(1));

        services.AddHostedService<IdentityBootstrap>();

        return services;
    }
}
