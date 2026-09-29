// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net.Http.Json;
using System.Text.Json;
using DDT.Contracts.Accounts;
using DDT.Contracts.Settings;
using DDT.Server.Settings;
using Xunit;

namespace DDT.Server.Tests;

// The Accounts page's requests, sent the way the page sends them.
// Writes carry the proof that the password was entered again.
internal static class AccountRequests
{
    public const string AccountsPath = "/api/accounts";

    public const string Password = "Share <pass> & \"7\"";

    // A new account. A test changes the fields it cares about with a with expression.
    public static SaveAccountRequest Request() =>
        new(0, $"Account {Guid.NewGuid():N}", @"CORP\svc-ddt", "corp.example", ["files.corp.example"], false, Secret(Password));

    // Sets the password, or keeps the stored one when it's null.
    public static SecretUpdate Secret(string? password) =>
        password is null ? new SecretUpdate(SecretAction.Keep, null) : new SecretUpdate(SecretAction.Set, password);

    // Saves the view unchanged and keeps the password.
    public static SaveAccountRequest Keep(AccountView view) =>
        new(view.Revision, view.Name, view.UserName, view.Domain, view.Hosts, view.RunAs, new SecretUpdate(SecretAction.Keep, null));

    public static async Task<HttpResponseMessage> SendAccountAsync(
        this SignedInClient client,
        HttpMethod method,
        string path,
        SaveAccountRequest? body,
        string? reauthentication)
    {
        using HttpRequestMessage request = new(method, new Uri(path, UriKind.Relative))
        {
            Content = body is null ? null : JsonContent.Create(body, options: TestJson.Options),
        };

        if (reauthentication is not null)
        {
            request.Headers.Add(ReauthenticationTokens.HeaderName, reauthentication);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static Task<HttpResponseMessage> CreateAccountAsync(this SignedInClient client, SaveAccountRequest request, string? reauthentication) =>
        client.SendAccountAsync(HttpMethod.Post, AccountsPath, request, reauthentication);

    public static Task<HttpResponseMessage> SaveAccountAsync(this SignedInClient client, Guid id, SaveAccountRequest request, string? reauthentication) =>
        client.SendAccountAsync(HttpMethod.Put, $"{AccountsPath}/{id:D}", request, reauthentication);

    public static Task<HttpResponseMessage> DeleteAccountAsync(this SignedInClient client, Guid id, string? reauthentication) =>
        client.SendAccountAsync(HttpMethod.Delete, $"{AccountsPath}/{id:D}", null, reauthentication);

    public static async Task<AccountView> CreatedAccountAsync(this SignedInClient client, SaveAccountRequest request) =>
        await RegisteredMachine.ReadAsync<AccountView>(await client.CreateAccountAsync(request, await client.TokenAsync()));

    public static async Task<AccountView> AccountAsync(this SignedInClient client, Guid id) =>
        await RegisteredMachine.ReadAsync<AccountView>(await client.GetAsync($"{AccountsPath}/{id:D}"));

    // The problem's code, read from the extension the page reads it from.
    public static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return problem.RootElement.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
    }
}
