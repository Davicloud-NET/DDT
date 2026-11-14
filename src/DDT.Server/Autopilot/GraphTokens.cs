// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace DDT.Server.Autopilot;

// App-only tokens for Microsoft Graph by the client credentials grant, kept until shortly before they expire.
// https://learn.microsoft.com/entra/identity-platform/v2-oauth2-client-creds-grant-flow
public sealed class GraphTokens(IHttpClientFactory clients, TimeProvider timeProvider)
{
    public const string Authority = "https://login.microsoftonline.com/";

    public const string Scope = "https://graph.microsoft.com/.default";

    // A token is fetched anew this long before it expires, so none runs out on the way.
    private static readonly TimeSpan s_margin = TimeSpan.FromMinutes(5);

    private readonly Lock _lock = new();
    private (string Key, string Token, DateTimeOffset Expires)? _cached;

    public static Uri TokenEndpoint(string tenant) => new($"{Authority}{Uri.EscapeDataString(tenant)}/oauth2/v2.0/token");

    public async Task<AuthenticationHeaderValue> HeaderAsync(AutopilotOptions account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        string key = Key(account);

        lock (_lock)
        {
            if (_cached is { } cached && cached.Key == key && cached.Expires > timeProvider.GetUtcNow())
            {
                return new AuthenticationHeaderValue("Bearer", cached.Token);
            }
        }

        (string token, TimeSpan lifetime) = await RequestAsync(account, cancellationToken).ConfigureAwait(false);

        lock (_lock)
        {
            _cached = (key, token, timeProvider.GetUtcNow() + lifetime - s_margin);
        }

        return new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<(string Token, TimeSpan Lifetime)> RequestAsync(AutopilotOptions account, CancellationToken cancellationToken)
    {
        using HttpClient client = clients.CreateClient(AutopilotServiceCollectionExtensions.GraphClient);
        using FormUrlEncodedContent form = new(
        [
            new("client_id", account.ClientId),
            new("scope", Scope),
            new("client_secret", account.ClientSecret),
            new("grant_type", "client_credentials"),
        ]);
        using HttpResponseMessage response = await client.PostAsync(TokenEndpoint(account.TenantId), form, cancellationToken).ConfigureAwait(false);
        using JsonAnswer answer = await JsonAnswer.ReadAsync(response, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            GraphAnswers.ThrowIfTransient(response);

            throw new AutopilotDirectoryException($"Microsoft Entra ID gave no token ({(int)response.StatusCode}): {answer.TokenError()}");
        }

        string token = answer.String("access_token") ?? throw new AutopilotDirectoryException("Microsoft Entra ID answered without a token.");
        int seconds = answer.Int("expires_in") ?? 3599;

        return (token, TimeSpan.FromSeconds(seconds));
    }

    // The secret only as a hash, so a changed secret fetches a new token.
    private static string Key(AutopilotOptions account) =>
        $"{account.TenantId}\n{account.ClientId}\n{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account.ClientSecret)))}";
}
