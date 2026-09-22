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

// A fresh deployment has no UI to create the first account from, so DDT creates one and prints
// the password once. No bootstrap credential is read from configuration, because an environment
// variable holding an administrator password tends to stay set long after it was needed.
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

        foreach (string role in DdtRoleNames.All)
        {
            if (!await roles.RoleExistsAsync(role).ConfigureAwait(false))
            {
                await roles.CreateAsync(new DdtRole(role)).ConfigureAwait(false);
            }
        }

        if (await database.Users.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

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
            // Without its role the account administers nothing, and while it exists no later start creates one that
            // does.
            IdentityResult deleted = await users.DeleteAsync(administrator).ConfigureAwait(false);
            LogBootstrapFailed(string.Join("; ", granted.Errors.Concat(deleted.Errors).Select(error => error.Description)));
            return;
        }

        LogAdministratorCreated(AdministratorUserName, password);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Identity requires a digit, an upper case and a lower case letter. About one draw in forty from
    // this alphabet has no digit, and the first administrator would silently never exist, so a draw
    // that misses a class is thrown away rather than patched, which keeps the result uniform.
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

    [LoggerMessage(
        EventId = 300,
        Level = LogLevel.Warning,
        Message = "Created the first administrator. User name {UserName}, password {Password}. This is printed once: sign in and change it.")]
    private partial void LogAdministratorCreated(string userName, string password);

    [LoggerMessage(EventId = 301, Level = LogLevel.Error, Message = "Could not create the first administrator: {Errors}")]
    private partial void LogBootstrapFailed(string errors);
}
