// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DDT.Server.Tests;

public sealed class IdentityBootstrapTests
{
    [Fact]
    public void AlwaysGeneratesAPasswordIdentityAccepts()
    {
        // About 2.6 percent of draws from the alphabet lack a digit, so ten thousand draws cannot all pass by luck.
        for (int draw = 0; draw < 10_000; draw++)
        {
            string password = IdentityBootstrap.GeneratePassword();

            Assert.Contains(password, char.IsAsciiDigit);
            Assert.Contains(password, char.IsAsciiLetterUpper);
            Assert.Contains(password, char.IsAsciiLetterLower);
        }
    }

    // An administrator without its role can't administer anything. As an existing account, it would also stop every
    // later start from creating one that can. So the bootstrap fails like when the account itself can't be created.
    [Fact]
    public async Task AFailedRoleAssignmentLeavesNoAdministrator()
    {
        using RoleRefusingApplication application = new();

        Assert.Empty(await application.QueryAsync(database => database.Users.ToListAsync(TestContext.Current.CancellationToken)));
        Assert.Contains(
            application.Log.Entries,
            entry => entry is { EventId.Id: 301, Level: LogLevel.Error }
                && entry.Message == "Could not create the first administrator: The test refuses every role.");
        Assert.False(application.Log.Logged(300, LogLevel.Warning));
    }

    // Without the roles, nobody could be made a viewer.
    // So the first administrator waits for a start that creates every role.
    [Fact]
    public async Task AFailedRoleCreationIsLoggedAndLeavesNoAdministrator()
    {
        using ViewerRefusingApplication application = new();

        Assert.Empty(await application.QueryAsync(database => database.Users.ToListAsync(TestContext.Current.CancellationToken)));
        Assert.Contains(
            application.Log.Entries,
            entry => entry is { EventId.Id: 302, Level: LogLevel.Error }
                && entry.Message == "Could not create the role Viewer: The test refuses the Viewer role.");
        Assert.False(application.Log.Logged(300, LogLevel.Warning));
    }
}
