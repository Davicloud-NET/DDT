// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Authentication;
using DDT.Server.Configuration;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Identity;

namespace DDT.Host.Startup;

// The recovery for what the settings page cannot fix, such as a section that locks everyone out. It needs only the
// database and the key ring, as in docker exec ddt ./DDT.Host settings reset ldap, next to the running servers.
public static class SettingsConsole
{
    private const string AdministratorUserName = "admin";

    public static bool Handles(string[] args) => args is ["settings", ..];

    // Settings are configuration for tests, which run the verbs against a store of their own.
    public static async Task<int> RunAsync(string[] args, TextWriter output, IEnumerable<KeyValuePair<string, string?>>? settings = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);

        switch (args)
        {
            case ["settings", "reset", string section] when SettingsDefinitions.Find(section) is { } definition:
                await using (WebApplication app = await StartAsync(settings).ConfigureAwait(false))
                {
                    await ResetAsync(app, definition).ConfigureAwait(false);
                }

                await output.WriteLineAsync(
                    $"Reset {definition.Name} to the defaults and cleared its secrets. Running servers apply it within 15 seconds. " +
                    "A key in configuration still overrides its field.").ConfigureAwait(false);

                return 0;

            case ["settings", "create-admin"] or ["settings", "create-admin", _]:
                string userName = args.Length > 2 ? args[2] : AdministratorUserName;

                await using (WebApplication app = await StartAsync(settings).ConfigureAwait(false))
                {
                    (string? password, string? problem) = await CreateAdministratorAsync(app, userName).ConfigureAwait(false);

                    if (problem is not null)
                    {
                        await output.WriteLineAsync(problem).ConfigureAwait(false);

                        return 1;
                    }

                    await output.WriteLineAsync(
                        $"{userName} is a local administrator that is enabled, with the password {password}. This is printed once: " +
                        "sign in and change it.").ConfigureAwait(false);
                }

                return 0;

            default:
                await output.WriteLineAsync(
                    "Usage: DDT.Host settings reset <section>, where section is one of " +
                    $"{string.Join(", ", SettingsDefinitions.All.Select(definition => definition.Name))}; " +
                    "or DDT.Host settings create-admin [user name].").ConfigureAwait(false);

                return 2;
        }
    }

    // The services a server has, but no listener: the schema is brought up to date, as a start would.
    private static async Task<WebApplication> StartAsync(IEnumerable<KeyValuePair<string, string?>>? settings)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });

        if (settings is not null)
        {
            builder.Configuration.AddInMemoryCollection(settings);
        }

        DdtOptions options = builder.Configuration.GetSection(DdtOptions.SectionName).Get<DdtOptions>() ?? new DdtOptions();

        builder.Services.AddDdtData(builder.Configuration, options);
        builder.Services.AddDdtSettings();
        builder.Services.AddDdtAuthentication(builder.Configuration, options);
        builder.Services.AddDdtMachines();

        WebApplication app = builder.Build();

        foreach (IHostedService initializer in app.Services.GetServices<IHostedService>().OfType<DatabaseInitializer>())
        {
            await initializer.StartAsync(CancellationToken.None).ConfigureAwait(false);
        }

        return app;
    }

    private static async Task ResetAsync(WebApplication app, SettingsSectionDefinition definition)
    {
        using IServiceScope scope = app.Services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<SettingsStore>().ResetAsync(definition, Actor.Console, CancellationToken.None).ConfigureAwait(false);
    }

    // A new local administrator, or the existing one enabled again, unlocked, with a new password and no second factor:
    // whoever runs this has the database and the key ring, which is more than any account grants.
    private static async Task<(string? Password, string? Problem)> CreateAdministratorAsync(WebApplication app, string userName)
    {
        using IServiceScope scope = app.Services.CreateScope();
        UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
        RoleManager<DdtRole> roles = scope.ServiceProvider.GetRequiredService<RoleManager<DdtRole>>();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        TimeProvider timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        string password = IdentityBootstrap.GeneratePassword();

        if (!await roles.RoleExistsAsync(DdtRoleNames.Administrator).ConfigureAwait(false)
            && Failed(await roles.CreateAsync(new DdtRole(DdtRoleNames.Administrator)).ConfigureAwait(false)) is { } roleProblem)
        {
            return (null, roleProblem);
        }

        DdtUser? user = await users.FindByNameAsync(userName).ConfigureAwait(false);
        bool created = user is null;

        if (user is not null && user.Source != AccountSource.Local)
        {
            return (null, $"{userName} signs in through a directory or single sign-on. Name a local account, or a new one.");
        }

        string? problem;

        if (user is null)
        {
            (user, problem) = await CreateLocalAsync(users, userName, password, timeProvider.GetUtcNow()).ConfigureAwait(false);
        }
        else
        {
            problem = await EnableAgainAsync(users, user, password).ConfigureAwait(false);
        }

        if (problem is not null)
        {
            return (null, problem);
        }

        if (!await users.IsInRoleAsync(user, DdtRoleNames.Administrator).ConfigureAwait(false)
            && Failed(await users.AddToRoleAsync(user, DdtRoleNames.Administrator).ConfigureAwait(false)) is { } grantProblem)
        {
            return (null, grantProblem);
        }

        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.AdministratorCreated,
            user.Id.ToString("D"),
            Actor.Console,
            timeProvider.GetUtcNow(),
            created
                ? $"Created the local administrator {userName} from the console."
                : $"Enabled the local administrator {userName} again from the console, with a new password and without a second factor."));

        await database.SaveChangesAsync().ConfigureAwait(false);

        return (password, null);
    }

    private static async Task<(DdtUser User, string? Problem)> CreateLocalAsync(
        UserManager<DdtUser> users,
        string userName,
        string password,
        DateTimeOffset now)
    {
        DdtUser user = new()
        {
            UserName = userName,
            DisplayName = "Administrator",
            Source = AccountSource.Local,
            CreatedUtc = now,
        };

        return (user, Failed(await users.CreateAsync(user, password).ConfigureAwait(false)));
    }

    // The new password changes the security stamp too, which signs the account out everywhere.
    private static async Task<string?> EnableAgainAsync(UserManager<DdtUser> users, DdtUser user, string password)
    {
        user.IsDisabled = false;

        Func<Task<IdentityResult>>[] steps =
        [
            () => users.UpdateAsync(user),
            () => users.SetLockoutEndDateAsync(user, null),
            () => users.ResetAccessFailedCountAsync(user),
            () => users.SetTwoFactorEnabledAsync(user, false),
            () => users.RemovePasswordAsync(user),
            () => users.AddPasswordAsync(user, password),
        ];

        foreach (Func<Task<IdentityResult>> step in steps)
        {
            if (Failed(await step().ConfigureAwait(false)) is { } problem)
            {
                return problem;
            }
        }

        return null;
    }

    private static string? Failed(IdentityResult result) =>
        result.Succeeded ? null : string.Join(" ", result.Errors.Select(error => error.Description));
}
