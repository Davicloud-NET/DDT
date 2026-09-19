using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using DDT.Contracts.Agents;

namespace DDT.Agent;

public sealed class HttpAgentServer : IAgentServer, IDisposable
{
    private const int MaxErrorDetailLength = 300;

    private static readonly TimeSpan s_connectTimeout = TimeSpan.FromSeconds(10);

    private readonly SocketsHttpHandler _handler;
    private readonly HttpClient _client;
    private readonly HttpClient _downloadClient;

    public HttpAgentServer(Uri serverUrl, X509Certificate2? rootCertificate)
        : this(serverUrl, rootCertificate, TimeSpan.FromSeconds(30))
    {
    }

    public HttpAgentServer(Uri serverUrl, X509Certificate2? rootCertificate, TimeSpan requestTimeout)
    {
        ArgumentNullException.ThrowIfNull(serverUrl);

        // No proxy: Windows PE has none, and looking for one loads WinHTTP for nothing.
        SocketsHttpHandler handler = new()
        {
            UseProxy = false,
            ConnectTimeout = s_connectTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };

        // Trust exactly DDT's root and nothing in the machine store. Revocation is not checked and missing
        // intermediates are not downloaded, because a provisioning network has no route to either and each
        // attempt stalls the handshake past the connect timeout. The server has to send its full chain.
        if (rootCertificate is not null)
        {
            handler.SslOptions.CertificateChainPolicy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                RevocationMode = X509RevocationMode.NoCheck,
                DisableCertificateDownloads = true,
                CustomTrustStore = { rootCertificate },
            };
        }

        _handler = handler;
        _client = new HttpClient(handler, disposeHandler: false) { BaseAddress = serverUrl, Timeout = requestTimeout };

        // A download can wait in the server's queue behind a whole lab for longer than a request may take,
        // so only its own deadline bounds it.
        _downloadClient = new HttpClient(handler, disposeHandler: false) { BaseAddress = serverUrl, Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<AgentRegistrationResult> RegisterAsync(AgentRegistration registration, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, AgentRoutes.Register)
        {
            Content = JsonContent.Create(registration, AgentJsonContext.Default.AgentRegistration),
        };

        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        return await ReadAsync(response, AgentJsonContext.Default.AgentRegistrationResult, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AgentNextResult> NextAsync(Guid machineId, string token, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, AgentRoutes.Next(machineId));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        return await ReadAsync(response, AgentJsonContext.Default.AgentNextResult, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendLogAsync(Guid machineId, string token, AgentLogBatch batch, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, AgentRoutes.Log(machineId))
        {
            Content = JsonContent.Create(batch, AgentJsonContext.Default.AgentLogBatch),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AgentSignInResult> SignInAsync(Guid machineId, string token, AgentSignInRequest request, CancellationToken cancellationToken)
    {
        using HttpRequestMessage message = new(HttpMethod.Post, AgentRoutes.SignIn(machineId))
        {
            Content = JsonContent.Create(request, AgentJsonContext.Default.AgentSignInRequest),
        };

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await SendAsync(message, cancellationToken).ConfigureAwait(false);

        return await ReadAsync(response, AgentJsonContext.Default.AgentSignInResult, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AgentRelease?> GetReleaseAsync(CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, AgentRoutes.Release);

        try
        {
            using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

            return await ReadAsync(response, AgentJsonContext.Default.AgentRelease, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DownloadReleaseAsync(Stream destination, CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(AgentLimits.DownloadTimeout);

        using HttpRequestMessage request = new(HttpMethod.Get, AgentRoutes.ReleaseBinary);
        using HttpResponseMessage response = await SendAsync(_downloadClient, request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);

        await response.Content.CopyToAsync(destination, deadline.Token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _client.Dispose();
        _downloadClient.Dispose();
        _handler.Dispose();
    }

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        SendAsync(_client, request, HttpCompletionOption.ResponseContentRead, cancellationToken);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        HttpCompletionOption completion,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await client.SendAsync(request, completion, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();

            throw new AgentTokenRejectedException($"The server refused the token for {request.RequestUri}.");
        }

        if (!response.IsSuccessStatusCode)
        {
            HttpStatusCode status = response.StatusCode;

            // A refusal such as a validation problem would otherwise be retried forever with no reason on
            // the console, which is all an operator standing at the machine can see.
            string detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            response.Dispose();

            if (detail.Length > MaxErrorDetailLength)
            {
                detail = detail[..MaxErrorDetailLength];
            }

            throw new HttpRequestException($"The server answered {(int)status} {status} for {request.RequestUri}: {detail}", null, status);
        }

        return response;
    }

    private static async Task<T> ReadAsync<T>(
        HttpResponseMessage response,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken).ConfigureAwait(false)
        ?? throw new HttpRequestException($"The server sent an empty answer for {response.RequestMessage?.RequestUri}.");
}
