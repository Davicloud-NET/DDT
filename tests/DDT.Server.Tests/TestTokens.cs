// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using DDT.Server.Data;
using DDT.Server.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// API tokens stored directly, for tests about what a token may do rather than how it is made. Sign-ins are limited per
// address, so a test class cannot sign in every user it needs a token for.
internal static class TestTokens
{
    public static Task<Guid> UserIdAsync(this DdtApplication application, string userName) =>
        application.QueryAsync(database => database.Users.Where(u => u.UserName == userName).Select(u => u.Id).SingleAsync(TestContext.Current.CancellationToken));

    public static async Task<(Guid Id, string Secret)> SeedTokenAsync(
        this DdtApplication application,
        string userName,
        string role,
        string? name = null,
        DateTimeOffset? expires = null)
    {
        Guid userId = await application.UserIdAsync(userName);
        string secret = ApiTokenSecrets.Create();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ApiToken token = new()
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            Name = name ?? $"token-{Guid.NewGuid():N}"[..20],
            Role = role,
            SecretHash = ApiTokenSecrets.Hash(secret),
            Hint = ApiTokenSecrets.Hint(secret),
            CreatedUtc = now,
            ExpiresUtc = expires ?? now.AddDays(30),
        };

        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        database.ApiTokens.Add(token);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (token.Id, secret);
    }

    public static async Task ChangeTokenAsync(this DdtApplication application, Guid tokenId, Action<ApiToken> change)
    {
        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        ApiToken token = await database.ApiTokens.SingleAsync(t => t.Id == tokenId, TestContext.Current.CancellationToken);

        change(token);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public static Task<ApiToken> TokenAsync(this DdtApplication application, Guid tokenId) =>
        application.QueryAsync(database => database.ApiTokens.AsNoTracking().SingleAsync(t => t.Id == tokenId, TestContext.Current.CancellationToken));

    // A script's client: the token in every request, no cookie and no antiforgery token.
    public static HttpClient TokenClient(this DdtApplication application, string secret, string? remoteAddress = null)
    {
        HttpClient client = application.CreateDefaultClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        if (remoteAddress is not null)
        {
            client.DefaultRequestHeaders.Add(TestRemoteAddress.Header, remoteAddress);
        }

        return client;
    }

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string path, object? body = null) =>
        client.PostAsync(
            new Uri(path, UriKind.Relative),
            body is null ? null : JsonContent.Create(body, options: TestJson.Options),
            TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> GetPathAsync(this HttpClient client, string path) =>
        client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> DeletePathAsync(this HttpClient client, string path) =>
        client.DeleteAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
}
