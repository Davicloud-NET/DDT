using System.Net;
using System.Net.Http.Json;

namespace DDT.Server.Tests;

// Carries the session cookie and the antiforgery token the way the SPA does: a token comes back on
// GET /api/auth/session and on every response that changes the identity.
public sealed class SignedInClient(HttpClient client, CookieContainer cookies) : IDisposable
{
    private const string CsrfHeader = "X-CSRF-TOKEN";

    private string? _csrfToken;

    public HttpClient Http => client;

    public CookieContainer Cookies => cookies;

    public async Task<HttpResponseMessage> PostAsync(string path, object? body = null)
    {
        if (_csrfToken is null)
        {
            using HttpResponseMessage session = await client.GetAsync(new Uri("/api/auth/session", UriKind.Relative));
            Remember(session);
        }

        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = body is null ? null : JsonContent.Create(body, options: TestJson.Options),
        };

        request.Headers.Add(CsrfHeader, _csrfToken);

        HttpResponseMessage response = await client.SendAsync(request);
        Remember(response);

        return response;
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
