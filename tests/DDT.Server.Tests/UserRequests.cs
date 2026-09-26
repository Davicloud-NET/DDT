// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using DDT.Contracts.Authentication;
using DDT.Contracts.Users;
using DDT.Server.Data;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

// Accounts made through the Users API, and browsers that sign in with them.
internal static class UserRequests
{
    public const string UsersApi = "/api/users";

    public static CreateUserRequest Local(string role) => new($"user-{Guid.NewGuid():N}", "Test User", null, role);

    public static async Task<CreatedUser> CreatedUserAsync(this SignedInClient client, string role) =>
        await RegisteredMachine.ReadAsync<CreatedUser>(await client.PostAsync(UsersApi, Local(role)));

    public static async Task<UserView> UserAsync(this SignedInClient client, Guid id) =>
        Assert.Single(await RegisteredMachine.ReadAsync<List<UserView>>(await client.GetAsync(UsersApi)), user => user.Id == id);

    // A browser of its own, from an address of its own, so that its sign-ins count against a window of their own.
    public static SignedInClient Browser(this DdtApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        CookieContainer cookies = new();
        HttpClient client = application.CreateDefaultClient(new CookieContainerHandler(cookies));
        client.DefaultRequestHeaders.Add(TestRemoteAddress.Header, TestRemoteAddress.Unique());

        return new SignedInClient(client, cookies);
    }

    // Null when the sign-in is refused outright.
    public static async Task<LoginStatus?> SignInAsync(this SignedInClient browser, string userName, string password)
    {
        ArgumentNullException.ThrowIfNull(browser);

        using HttpResponseMessage response = await browser.PostAsync("/api/auth/login", new LoginRequest(userName, password, null, null));

        return response.StatusCode == HttpStatusCode.Unauthorized
            ? null
            : (await response.Content.ReadFromJsonAsync<LoginResponse>(TestJson.Options, TestContext.Current.CancellationToken))?.Status;
    }

    public static async Task<SignedInClient> SignedInBrowserAsync(this DdtApplication application, string userName, string password)
    {
        SignedInClient browser = application.Browser();
        Assert.Equal(LoginStatus.Succeeded, await browser.SignInAsync(userName, password));

        return browser;
    }

    public static Task<List<AuditEvent>> UserAuditAsync(this DdtApplication application, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(application);

        string subject = userId.ToString("D");

        return application.QueryAsync(database => database.AuditEvents
            .AsNoTracking()
            .Where(e => e.SubjectId == subject)
            .OrderBy(e => e.Id)
            .ToListAsync(TestContext.Current.CancellationToken));
    }
}
