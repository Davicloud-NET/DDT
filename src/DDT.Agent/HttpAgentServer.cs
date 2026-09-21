// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using DDT.Contracts.Agents;

namespace DDT.Agent;

public sealed class HttpAgentServer : IAgentServer, IDisposable
{
    private const int MaxErrorDetailLength = 300;

    private static readonly TimeSpan s_connectTimeout = TimeSpan.FromSeconds(10);

    private readonly SocketsHttpHandler _handler;
    private readonly HttpClient _client;
    private readonly HttpClient _downloadClient;

    // What DDT names its roots, so a refused certificate from one can be told apart from an administrator's.
    private const string DdtRootSubjectPrefix = "CN=DDT root ";

    // How the server's certificate fared against the pinned root in the latest handshake, and who issued it, so a
    // refused connection can say what to fix.
    private int _certificateErrors;
    private string? _certificateIssuer;

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

            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
            {
                Volatile.Write(ref _certificateErrors, (int)errors);
                Volatile.Write(ref _certificateIssuer, certificate?.Issuer);

                return errors == SslPolicyErrors.None;
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

    public async Task<IReadOnlyList<AgentImageChoice>> GetImagesAsync(Guid machineId, string token, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, AgentRoutes.Images(machineId));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        return await ReadAsync(response, AgentJsonContext.Default.IReadOnlyListAgentImageChoice, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AgentDeployment> PickImageAsync(Guid machineId, string token, AgentPickRequest request, CancellationToken cancellationToken)
    {
        using HttpRequestMessage message = new(HttpMethod.Post, AgentRoutes.Deployments(machineId))
        {
            Content = JsonContent.Create(request, AgentJsonContext.Default.AgentPickRequest),
        };

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await SendAsync(message, cancellationToken).ConfigureAwait(false);

        return await ReadAsync(response, AgentJsonContext.Default.AgentDeployment, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AgentDeploymentReportResult> ReportDeploymentAsync(
        Guid machineId,
        string token,
        Guid deploymentId,
        AgentDeploymentReport report,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, AgentRoutes.DeploymentReport(machineId, deploymentId))
        {
            Content = JsonContent.Create(report, AgentJsonContext.Default.AgentDeploymentReport),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        return await ReadAsync(response, AgentJsonContext.Default.AgentDeploymentReportResult, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetUnattendAsync(Guid machineId, string token, Guid deploymentId, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, AgentRoutes.DeploymentUnattend(machineId, deploymentId));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    // A GET of the first byte rather than HEAD: an answer to HEAD has no body, so a refusal would lose the server's
    // reason. Only the headers are read, in case a server ignores the range and sends the whole image.
    public async Task<long?> HeadImageAsync(Guid machineId, string token, string sha256, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, AgentRoutes.ImageContent(machineId, sha256));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Range = new RangeHeaderValue(0, 0);

        using HttpResponseMessage response = await SendAsync(_client, request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        return response.StatusCode == HttpStatusCode.PartialContent
            ? response.Content.Headers.ContentRange?.Length
            : response.Content.Headers.ContentLength;
    }

    // Through the download client: an image takes far longer than a request may, and a stalled read is caught by
    // the caller instead.
    public async Task<AgentImageStream> OpenImageAsync(
        Guid machineId,
        string token,
        string sha256,
        long offset,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        using HttpRequestMessage request = new(HttpMethod.Get, AgentRoutes.ImageContent(machineId, sha256));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (offset > 0)
        {
            request.Headers.Range = new RangeHeaderValue(offset, null);
        }

        HttpResponseMessage response = await SendAsync(_downloadClient, request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            long start = 0;
            long total = response.Content.Headers.ContentLength ?? -1;

            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                ContentRangeHeaderValue range = response.Content.Headers.ContentRange
                    ?? throw new HttpRequestException($"The server sent part of {request.RequestUri} without saying which part.");

                start = range.From ?? -1;
                total = range.Length ?? -1;
            }

            Stream content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            return new AgentImageStream(content, start, total, response);
        }
        catch
        {
            response.Dispose();

            throw;
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _downloadClient.Dispose();
        _handler.Dispose();
    }

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        SendAsync(_client, request, HttpCompletionOption.ResponseContentRead, cancellationToken);

    private async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        HttpCompletionOption completion,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, completion, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.HttpRequestError == HttpRequestError.SecureConnectionError
            && CertificateProblem(request.RequestUri) is { } problem)
        {
            throw new HttpRequestException(HttpRequestError.SecureConnectionError, problem, exception);
        }

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

            string? title = ProblemTitle(detail);

            if (detail.Length > MaxErrorDetailLength)
            {
                detail = detail[..MaxErrorDetailLength];
            }

            throw new AgentRequestException($"The server answered {(int)status} {status} for {request.RequestUri}: {detail}", title, status);
        }

        return response;
    }

    // The console is all a technician at the machine sees, so a refused certificate names its fix. A boot image built
    // before the server had its own root pins the old self-signed certificate, and fails here once, after the upgrade.
    private string? CertificateProblem(Uri? requestUri)
    {
        SslPolicyErrors errors = (SslPolicyErrors)Volatile.Read(ref _certificateErrors);

        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
        {
            string? issuer = Volatile.Read(ref _certificateIssuer);

            if (issuer is not null && issuer.StartsWith(DdtRootSubjectPrefix, StringComparison.Ordinal))
            {
                return "The server's certificate does not come from the root certificate this boot image trusts. Build the " +
                    "boot image again with Build-BootImage.ps1 -RootCertificatePath set to the server's ddt-root.pem, which is " +
                    "next to its certificate (/var/lib/ddt/certs/ddt-root.pem in the container). A server that moved to its " +
                    "own root needs this once.";
            }

            return $"The server's certificate, issued by {issuer}, does not chain to the root certificate this boot image " +
                "trusts. Build the boot image with Build-BootImage.ps1 -RootCertificatePath set to the root of the CA that " +
                "issued it, and have the server send the intermediate certificates after its own in the certificate file, " +
                "because the agent does not download them.";
        }

        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
        {
            return $"The server's certificate does not name {requestUri?.Host}. Add that name to DDT:Https:SubjectAlternativeNames " +
                "on the server and restart it: it then issues a certificate with the name from the same root.";
        }

        return null;
    }

    private static string? ProblemTitle(string body)
    {
        if (body.Length == 0)
        {
            return null;
        }

        try
        {
            using JsonDocument problem = JsonDocument.Parse(body);

            return problem.RootElement.ValueKind == JsonValueKind.Object
                && problem.RootElement.TryGetProperty("title", out JsonElement title)
                && title.ValueKind == JsonValueKind.String
                ? title.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<T> ReadAsync<T>(
        HttpResponseMessage response,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken).ConfigureAwait(false)
        ?? throw new HttpRequestException($"The server sent an empty answer for {response.RequestMessage?.RequestUri}.");
}
