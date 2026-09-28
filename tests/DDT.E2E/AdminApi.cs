// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization.Metadata;
using DDT.Contracts;
using DDT.Contracts.Authentication;
using DDT.Contracts.Images;
using DDT.Contracts.Packages;
using DDT.Contracts.Settings;
using DDT.Server.Settings;

namespace DDT.E2E;

// The web API as the browser uses it: a session cookie, and the CSRF token on every request that changes something.
internal sealed class AdminApi : IDisposable
{
    private const string CsrfHeader = "X-CSRF-TOKEN";

    private readonly HttpClient _client;
    private string? _csrfToken;

    public AdminApi(Uri baseAddress, X509Certificate2 rootCertificate)
    {
        BaseAddress = baseAddress;
        _client = new HttpClient(Handler(rootCertificate, Cookies)) { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(60) };
    }

    public Uri BaseAddress { get; }

    public CookieContainer Cookies { get; } = new();

    // Trusts DDT's root and nothing else, like the agent.
    public static SocketsHttpHandler Handler(X509Certificate2 rootCertificate, CookieContainer? cookies)
    {
        SocketsHttpHandler handler = new()
        {
            UseProxy = false,
            UseCookies = cookies is not null,
        };

        if (cookies is not null)
        {
            handler.CookieContainer = cookies;
        }

        handler.SslOptions.CertificateChainPolicy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            CustomTrustStore = { rootCertificate },
        };
        handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, errors) => errors == SslPolicyErrors.None;

        return handler;
    }

    public async Task SignInAsync(string userName, string password, CancellationToken cancellationToken)
    {
        await RefreshCsrfTokenAsync(cancellationToken).ConfigureAwait(false);
        LoginResponse response = await SendAsync(
            new JsonRequest<LoginRequest>(
                HttpMethod.Post,
                "api/auth/login",
                new LoginRequest(userName, password, null, null),
                DdtJsonContext.Default.LoginRequest),
            DdtJsonContext.Default.LoginResponse,
            HttpStatusCode.OK,
            cancellationToken).ConfigureAwait(false);

        if (response.Status != LoginStatus.Succeeded)
        {
            throw new InvalidOperationException($"Signing in as {userName} ended with {response.Status}.");
        }

        // The token is bound to the identity, so the token from before the sign-in is no longer valid.
        await RefreshCsrfTokenAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<T> GetAsync<T>(string path, JsonTypeInfo<T> type, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _client.GetAsync(new Uri(path, UriKind.Relative), cancellationToken).ConfigureAwait(false);
        await ExpectAsync(response, HttpStatusCode.OK, cancellationToken).ConfigureAwait(false);

        return (await response.Content.ReadFromJsonAsync(type, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<TResult> SendAsync<TBody, TResult>(
        JsonRequest<TBody> request,
        JsonTypeInfo<TResult> resultType,
        HttpStatusCode expected,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage message = request.ToMessage();
        using HttpResponseMessage response = await SendAsync(message, cancellationToken).ConfigureAwait(false);
        await ExpectAsync(response, expected, cancellationToken).ConfigureAwait(false);

        return (await response.Content.ReadFromJsonAsync(resultType, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<TResult> SendAsync<TResult>(
        HttpMethod method,
        string path,
        JsonTypeInfo<TResult> resultType,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, new Uri(path, UriKind.Relative));
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        await ExpectAsync(response, HttpStatusCode.OK, cancellationToken).ConfigureAwait(false);

        return (await response.Content.ReadFromJsonAsync(resultType, cancellationToken).ConfigureAwait(false))!;
    }

    // A write that needs the signed-in user's password again, such as saving an account. The proof goes with the
    // request, the same way the page sends it after its dialog.
    public async Task<TResult> SendReauthenticatedAsync<TBody, TResult>(
        JsonRequest<TBody> request,
        string password,
        JsonTypeInfo<TResult> resultType,
        HttpStatusCode expected,
        CancellationToken cancellationToken)
    {
        ReauthenticationToken proof = await SendAsync(
            new JsonRequest<ReauthenticateRequest>(
                HttpMethod.Post,
                "api/settings/reauthenticate",
                new ReauthenticateRequest(password, null),
                DdtJsonContext.Default.ReauthenticateRequest),
            DdtJsonContext.Default.ReauthenticationToken,
            HttpStatusCode.OK,
            cancellationToken).ConfigureAwait(false);

        using HttpRequestMessage message = request.ToMessage();
        message.Headers.Add(ReauthenticationTokens.HeaderName, proof.Token);
        using HttpResponseMessage response = await SendAsync(message, cancellationToken).ConfigureAwait(false);
        await ExpectAsync(response, expected, cancellationToken).ConfigureAwait(false);

        return (await response.Content.ReadFromJsonAsync(resultType, cancellationToken).ConfigureAwait(false))!;
    }

    // For a request the server should refuse. Returns the body of the response.
    public async Task<string> SendRefusedAsync<TBody>(
        JsonRequest<TBody> request,
        HttpStatusCode expected,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage message = request.ToMessage();
        using HttpResponseMessage response = await SendAsync(message, cancellationToken).ConfigureAwait(false);
        await ExpectAsync(response, expected, cancellationToken).ConfigureAwait(false);

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    // Deleting a rule returns the remaining rules, because their positions moved. Every other delete returns nothing.
    public async Task DeleteAsync(string path, HttpStatusCode expected, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Delete, new Uri(path, UriKind.Relative));
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        await ExpectAsync(response, expected, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ImageSummary>> UploadImageAsync(string file, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await UploadAsync(file, UploadKind.Image, cancellationToken).ConfigureAwait(false);

        return (await response.Content.ReadFromJsonAsync(DdtJsonContext.Default.IReadOnlyListImageSummary, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<PackageSummary> UploadPackageAsync(string file, UploadKind kind, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await UploadAsync(file, kind, cancellationToken).ConfigureAwait(false);

        return (await response.Content.ReadFromJsonAsync(DdtJsonContext.Default.PackageSummary, cancellationToken).ConfigureAwait(false))!;
    }

    public void Dispose() => _client.Dispose();

    // The upload protocol of the web UI: a session for the file, its chunks in order, then completion.
    private async Task<HttpResponseMessage> UploadAsync(string file, UploadKind kind, CancellationToken cancellationToken)
    {
        FileInfo info = new(file);
        ImageUploadSession session = await SendAsync(
            new JsonRequest<CreateImageUploadRequest>(
                HttpMethod.Post,
                "api/images/uploads",
                new CreateImageUploadRequest(info.Name, info.Length, new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds(), kind),
                DdtJsonContext.Default.CreateImageUploadRequest),
            DdtJsonContext.Default.ImageUploadSession,
            HttpStatusCode.Created,
            cancellationToken).ConfigureAwait(false);

        byte[] buffer = new byte[session.ChunkBytes];
        FileStream content = File.OpenRead(file);

        await using (content.ConfigureAwait(false))
        {
            for (long offset = session.Offset; offset < info.Length;)
            {
                content.Position = offset;
                int length = await content.ReadAtLeastAsync(buffer, (int)Math.Min(buffer.Length, info.Length - offset), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                using HttpRequestMessage chunk = new(HttpMethod.Patch, new Uri($"api/images/uploads/{session.Id:D}", UriKind.Relative))
                {
                    Content = new ByteArrayContent(buffer, 0, length),
                };

                chunk.Headers.Add("Upload-Offset", offset.ToString(CultureInfo.InvariantCulture));
                using HttpResponseMessage appended = await SendAsync(chunk, cancellationToken).ConfigureAwait(false);
                await ExpectAsync(appended, HttpStatusCode.NoContent, cancellationToken).ConfigureAwait(false);
                offset = long.Parse(appended.Headers.GetValues("Upload-Offset").Single(), CultureInfo.InvariantCulture);
            }
        }

        using HttpRequestMessage complete = new(HttpMethod.Post, new Uri($"api/images/uploads/{session.Id:D}/complete", UriKind.Relative));
        HttpResponseMessage completed = await SendAsync(complete, cancellationToken).ConfigureAwait(false);

        try
        {
            await ExpectAsync(completed, HttpStatusCode.Created, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            completed.Dispose();
            throw;
        }

        return completed;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Add(CsrfHeader, _csrfToken);
        HttpResponseMessage response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.Headers.TryGetValues(CsrfHeader, out IEnumerable<string>? rotated))
        {
            _csrfToken = rotated.Single();
        }

        return response;
    }

    private async Task RefreshCsrfTokenAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _client.GetAsync(new Uri("api/auth/session", UriKind.Relative), cancellationToken).ConfigureAwait(false);
        await ExpectAsync(response, HttpStatusCode.OK, cancellationToken).ConfigureAwait(false);
        _csrfToken = response.Headers.GetValues(CsrfHeader).Single();
    }

    private static async Task ExpectAsync(HttpResponseMessage response, HttpStatusCode expected, CancellationToken cancellationToken)
    {
        if (response.StatusCode != expected)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            throw new HttpRequestException(
                $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} answered {(int)response.StatusCode} {response.StatusCode} " +
                $"instead of {(int)expected} {expected}: {body}",
                null,
                response.StatusCode);
        }
    }
}
