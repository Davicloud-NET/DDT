// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Authentication;
using DDT.Server.Authentication;
using DDT.Server.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DDT.Server.Tests;

public sealed class FirstAdministratorFileTests
{
    [Fact]
    public async Task TheFirstStartPutsThePasswordInTheStoreAndOnlyThePathInTheLog()
    {
        using LoggedApplication application = new();
        _ = application.Server;
        FirstAdministratorFile file = application.Services.GetRequiredService<FirstAdministratorFile>();

        (string userName, string password) = Assert.NotNull(file.Read());
        Assert.Equal("admin", userName);
        Assert.StartsWith(application.StorePath, file.Path, StringComparison.Ordinal);
        Assert.True(await SignsInAsync(application, userName, password));

        string logged = Assert.Single(application.Log.Entries, entry => entry is { EventId.Id: 300, Level: LogLevel.Warning }).Message;
        Assert.Equal($"Created the first administrator admin. The password is in {file.Path}: sign in and change it.", logged);
        Assert.DoesNotContain(password, logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangingThePasswordDeletesTheFile()
    {
        using DdtApplication application = new();
        _ = application.Server;
        FirstAdministratorFile file = application.Services.GetRequiredService<FirstAdministratorFile>();
        (string userName, string password) = Assert.NotNull(file.Read());

        using SignedInClient browser = await application.SignedInBrowserAsync(userName, password);
        (await browser.PostAsync("/api/auth/password", new ChangePasswordRequest(password, "A password only I know 7"))).EnsureSuccessStatusCode();

        Assert.False(File.Exists(file.Path));
    }

    [Fact]
    public async Task AStartDeletesTheFileOnceItsPasswordNoLongerWorks()
    {
        using DdtApplication application = new();
        _ = application.Server;
        FirstAdministratorFile file = application.Services.GetRequiredService<FirstAdministratorFile>();
        (string userName, string password) = Assert.NotNull(file.Read());

        using (IServiceScope scope = application.Services.CreateScope())
        {
            UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
            DdtUser? user = await users.FindByNameAsync(userName);
            Assert.NotNull(user);
            Assert.True((await users.ChangePasswordAsync(user, password, "Set by another administrator 7")).Succeeded);
        }

        await application.Services.GetServices<IHostedService>()
            .OfType<IdentityBootstrap>()
            .Single()
            .StartAsync(TestContext.Current.CancellationToken);

        Assert.False(File.Exists(file.Path));
    }

    private static async Task<bool> SignsInAsync(DdtApplication application, string userName, string password)
    {
        using IServiceScope scope = application.Services.CreateScope();
        UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();

        return await users.FindByNameAsync(userName) is { } user && await users.CheckPasswordAsync(user, password);
    }
}
