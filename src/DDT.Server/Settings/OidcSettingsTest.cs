// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts.Messages;
using DDT.Contracts.Settings;

namespace DDT.Server.Settings;

// Reads the provider's discovery document, as the handler will at its first sign-in.
internal sealed class OidcSettingsTest(IHttpClientFactory clients)
{
    public async Task<OidcTestResult> TestAsync(Uri authority, string redirectUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authority);

        Uri discovery = new(authority.AbsoluteUri.TrimEnd('/') + "/.well-known/openid-configuration");
        string url = discovery.ToString();

        OidcTestResult Result(bool reached, string? issuer, ServerMessage message) => new(reached, issuer, redirectUri, message.Text, message);

        try
        {
            using HttpClient client = clients.CreateClient(SettingsServiceCollectionExtensions.OidcTestClient);
            using HttpResponseMessage response = await client.GetAsync(discovery, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Result(
                    false,
                    null,
                    ServerMessages.SettingsOidcTestAnswered.With("url", url, "status", (int)response.StatusCode, "reason", response.ReasonPhrase ?? string.Empty));
            }

            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
            string? issuer = document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("issuer", out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;

            if (issuer is null)
            {
                return Result(false, null, ServerMessages.SettingsOidcTestNotDiscovery.With("url", url));
            }

            // Tokens name their issuer, and the handler refuses one that differs from the authority it was given.
            ServerMessage message = string.Equals(issuer.TrimEnd('/'), authority.AbsoluteUri.TrimEnd('/'), StringComparison.Ordinal)
                ? ServerMessages.SettingsOidcTestReached.With("issuer", issuer, "redirectUri", redirectUri)
                : ServerMessages.SettingsOidcTestOtherIssuer.With("issuer", issuer, "authority", authority.ToString());

            return Result(true, issuer, message);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Result(false, null, ServerMessages.SettingsOidcTestUnreadable.With("url", url, "error", exception.Message));
        }
    }
}
