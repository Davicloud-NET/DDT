// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net.Http.Json;
using System.Text.Json;
using DDT.Contracts.Settings;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// The settings page's requests, as the page sends them.
internal static class SettingsRequests
{
    public static async Task<SettingsSectionView<T>> SectionAsync<T>(this SignedInClient client, string section) =>
        await RegisteredMachine.ReadAsync<SettingsSectionView<T>>(await client.GetAsync($"/api/settings/{section}"));

    public static async Task<HttpResponseMessage> SaveAsync<T>(
        this SignedInClient client,
        string section,
        long version,
        T values,
        Dictionary<string, SecretUpdate>? secrets = null,
        IReadOnlyList<string>? confirm = null,
        string? reauthentication = null,
        string? proof = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, new Uri($"/api/settings/{section}", UriKind.Relative))
        {
            Content = JsonContent.Create(new SettingsSectionUpdate<T>(version, values, secrets, confirm), options: TestJson.Options),
        };

        if (reauthentication is not null)
        {
            request.Headers.Add(ReauthenticationTokens.HeaderName, reauthentication);
        }

        if (proof is not null)
        {
            request.Headers.Add(DirectoryProofs.HeaderName, proof);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    // Saves and reads the view back, for tests that only prepare a section.
    public static async Task<SettingsSectionView<T>> SavedAsync<T>(
        this SignedInClient client,
        string section,
        Func<T, T> change,
        Dictionary<string, SecretUpdate>? secrets = null,
        IReadOnlyList<string>? confirm = null,
        string? reauthentication = null)
    {
        SettingsSectionView<T> current = await client.SectionAsync<T>(section);

        return await RegisteredMachine.ReadAsync<SettingsSectionView<T>>(
            await client.SaveAsync(section, current.Version, change(current.Values), secrets, confirm, reauthentication));
    }

    // Every re-authentication counts against the sign-in limit of its address, so each comes from an address of its own.
    public static async Task<HttpResponseMessage> ReauthenticateAsync(this SignedInClient client, string password = DdtApplication.Password, string? code = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri("/api/settings/reauthenticate", UriKind.Relative))
        {
            Content = JsonContent.Create(new ReauthenticateRequest(password, code), options: TestJson.Options),
        };

        request.Headers.Add(TestRemoteAddress.Header, TestRemoteAddress.Unique());

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static async Task<string> TokenAsync(this SignedInClient client) =>
        (await RegisteredMachine.ReadAsync<ReauthenticationToken>(await client.ReauthenticateAsync())).Token;

    public static async Task<HttpValidationProblemDetails> ProblemsAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(TestJson.Options, TestContext.Current.CancellationToken))!;

    // The codes of the warnings a save still has to confirm, from the extension the page reads them from.
    public static async Task<List<string>> UnconfirmedAsync(HttpResponseMessage response)
    {
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return [.. problem.RootElement.GetProperty("confirm").EnumerateArray().Select(warning => warning.GetProperty("code").GetString()!)];
    }

    public static Task RefreshSettingsAsync(this DdtApplication application) =>
        application.Services.GetRequiredService<SettingsService>().RefreshAsync(TestContext.Current.CancellationToken);
}
