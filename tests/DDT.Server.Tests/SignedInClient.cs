// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;

namespace DDT.Server.Tests;

// Carries the session cookie and the antiforgery token the way the SPA does.
// A token comes back on GET /api/auth/session and on every response that changes the identity.
public sealed class SignedInClient(HttpClient client, CookieContainer cookies) : IDisposable
{
    private const string CsrfHeader = "X-CSRF-TOKEN";

    private string? _csrfToken;

    public HttpClient Http => client;

    public CookieContainer Cookies => cookies;

    public Task<HttpResponseMessage> PostAsync(string path, object? body = null) => SendAsync(HttpMethod.Post, path, body);

    public Task<HttpResponseMessage> PutAsync(string path, object? body) => SendAsync(HttpMethod.Put, path, body);

    public Task<HttpResponseMessage> PatchAsync(string path, object? body) => SendAsync(HttpMethod.Patch, path, body);

    public Task<HttpResponseMessage> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path, null);

    // Sends any request with its own content and headers, such as an upload chunk. The antiforgery token is added here.
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_csrfToken is null)
        {
            using HttpResponseMessage session = await client.GetAsync(new Uri("/api/auth/session", UriKind.Relative), cancellationToken);
            Remember(session);
        }

        request.Headers.Add(CsrfHeader, _csrfToken);

        HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        Remember(response);

        return response;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body)
    {
        using HttpRequestMessage request = new(method, new Uri(path, UriKind.Relative))
        {
            Content = body is null ? null : JsonContent.Create(body, options: TestJson.Options),
        };

        return await SendAsync(request);
    }

    public Task<HttpResponseMessage> GetAsync(string path) => client.GetAsync(new Uri(path, UriKind.Relative));

    public void Dispose() => client.Dispose();

    private void Remember(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues(CsrfHeader, out IEnumerable<string>? values))
        {
            _csrfToken = values.First();
        }
    }
}
