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
    private static readonly TimeSpan s_requestTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _client;
    private readonly string _enrollmentToken;

    public HttpAgentServer(Uri serverUrl, string enrollmentToken, X509Certificate2? rootCertificate)
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

        _client = new HttpClient(handler) { BaseAddress = serverUrl, Timeout = s_requestTimeout };
        _enrollmentToken = enrollmentToken;
    }

    public async Task<AgentRegistrationResult> RegisterAsync(AgentRegistration registration, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, AgentRoutes.Register)
        {
            Content = JsonContent.Create(registration, AgentJsonContext.Default.AgentRegistration),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _enrollmentToken);

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

    public void Dispose() => _client.Dispose();

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);

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
