// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Server.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Authentication;

// A fresh deployment has no UI to make the first account in, so this makes one and logs its password once. No password
// comes from configuration: such an environment variable tends to stay set long after.
public sealed partial class IdentityBootstrap(
    IServiceScopeFactory scopeFactory,
    ILogger<IdentityBootstrap> logger) : IHostedService
{
    private const string AdministratorUserName = "admin";
    private const string PasswordAlphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int PasswordLength = 24;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();

        RoleManager<DdtRole> roles = scope.ServiceProvider.GetRequiredService<RoleManager<DdtRole>>();
        UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();

        if (!await CreateRolesAsync(roles).ConfigureAwait(false))
        {
            return;
        }

        // A directory account always has the directory's id. One without is single sign-on's, and a password typed for
        // it must never reach the directory, which could take it over for a directory user of the same name.
        await database.Users
            .Where(u => u.Source == AccountSource.Directory && u.DirectoryObjectId == null)
            .ExecuteUpdateAsync(user => user.SetProperty(u => u.Source, AccountSource.External), cancellationToken)
            .ConfigureAwait(false);

        if (await database.Users.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await CreateAdministratorAsync(users).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Identity requires a digit, an upper and a lower case letter, which about one draw in forty lacks, and the first
    // administrator would then silently never exist. Such a draw is thrown away rather than patched, which keeps the
    // result uniform.
    public static string GeneratePassword()
    {
        string password;

        do
        {
            password = RandomNumberGenerator.GetString(PasswordAlphabet, PasswordLength);
        }
        while (!password.Any(char.IsAsciiDigit) || !password.Any(char.IsAsciiLetterUpper) || !password.Any(char.IsAsciiLetterLower));

        return password;
    }

    // Every start creates the roles that are missing, so the next one tries again. Until then no first administrator is
    // created: it might lack its role.
    private async Task<bool> CreateRolesAsync(RoleManager<DdtRole> roles)
    {
        foreach (string role in DdtRoleNames.All)
        {
            if (await roles.RoleExistsAsync(role).ConfigureAwait(false))
            {
                continue;
            }

            IdentityResult added = await roles.CreateAsync(new DdtRole(role)).ConfigureAwait(false);

            if (!added.Succeeded)
            {
                LogRoleFailed(role, string.Join("; ", added.Errors.Select(error => error.Description)));
                return false;
            }
        }

        return true;
    }

    private async Task CreateAdministratorAsync(UserManager<DdtUser> users)
    {
        string password = GeneratePassword();

        DdtUser administrator = new()
        {
            UserName = AdministratorUserName,
            DisplayName = "Administrator",
            Source = AccountSource.Local,
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        IdentityResult created = await users.CreateAsync(administrator, password).ConfigureAwait(false);

        if (!created.Succeeded)
        {
            LogBootstrapFailed(string.Join("; ", created.Errors.Select(error => error.Description)));
            return;
        }

        IdentityResult granted = await users.AddToRoleAsync(administrator, DdtRoleNames.Administrator).ConfigureAwait(false);

        if (!granted.Succeeded)
        {
            // Without its role the account administers nothing, and while it exists no later start makes one that does.
            IdentityResult deleted = await users.DeleteAsync(administrator).ConfigureAwait(false);
            LogBootstrapFailed(string.Join("; ", granted.Errors.Concat(deleted.Errors).Select(error => error.Description)));
            return;
        }

        LogAdministratorCreated(AdministratorUserName, password);
    }

    [LoggerMessage(
        EventId = 300,
        Level = LogLevel.Warning,
        Message = "Created the first administrator. User name {UserName}, password {Password}. This is printed once: sign in and change it.")]
    private partial void LogAdministratorCreated(string userName, string password);

    [LoggerMessage(EventId = 301, Level = LogLevel.Error, Message = "Could not create the first administrator: {Errors}")]
    private partial void LogBootstrapFailed(string errors);

    [LoggerMessage(EventId = 302, Level = LogLevel.Error, Message = "Could not create the role {Role}: {Errors}")]
    private partial void LogRoleFailed(string role, string errors);
}
