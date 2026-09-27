// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Authentication;
using DDT.Contracts.Users;
using DDT.Server.Authentication;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

// Without a group map the directory only checks passwords, and administrators give directory accounts their roles.
public sealed class UnmappedDirectoryTests(UnmappedDirectoryApplication application) : IClassFixture<UnmappedDirectoryApplication>
{
    [Fact]
    public async Task AdministratorsGiveDirectoryAccountsTheirRoles()
    {
        string userName = $"tech-{Guid.NewGuid():N}";
        SignedInClient administrator = await application.AdministratorAsync();

        DirectoryCheck before = await RegisteredMachine.ReadAsync<DirectoryCheck>(
            await administrator.PostAsync("/api/directory/check", new DirectoryCheckRequest(userName)));
        Assert.Null(before.Role);
        Assert.Empty(before.Matches);
        Assert.StartsWith("The directory's group map on the Sign-in page is empty, so administrators set roles. A first sign-in", before.Message, StringComparison.Ordinal);

        using (SignedInClient browser = application.Browser())
        {
            Assert.Equal(LoginStatus.Succeeded, await browser.SignInAsync(userName, DdtApplication.Password));
            Assert.Empty((await RegisteredMachine.ReadAsync<CurrentUser>(await browser.GetAsync("/api/auth/me"))).Roles);
        }

        Guid id = await application.QueryAsync(database => database.Users.Where(u => u.UserName == userName).Select(u => u.Id).SingleAsync(TestContext.Current.CancellationToken));
        UserView unassigned = await administrator.UserAsync(id);
        Assert.Null(unassigned.Role);
        Assert.Null(unassigned.RoleFrom);

        UserView assigned = await RegisteredMachine.ReadAsync<UserView>(
            await administrator.PatchAsync($"/api/users/{id}", new UpdateUserRequest(null, null, DdtRoleNames.Operator)));
        Assert.Equal(RoleSource.Manual, assigned.RoleFrom);

        // The next sign-in leaves the role alone.
        using SignedInClient again = await application.SignedInBrowserAsync(userName, DdtApplication.Password);
        Assert.Equal([DdtRoleNames.Operator], (await RegisteredMachine.ReadAsync<CurrentUser>(await again.GetAsync("/api/auth/me"))).Roles);
        Assert.Equal(
            "The directory's group map on the Sign-in page is empty, so administrators set roles. The account has Operator.",
            (await RegisteredMachine.ReadAsync<DirectoryCheck>(await administrator.PostAsync("/api/directory/check", new DirectoryCheckRequest(userName)))).Message);
    }
}
