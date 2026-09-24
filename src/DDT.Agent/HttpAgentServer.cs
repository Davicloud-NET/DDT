// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
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

    private static readonly TimeSpan s_requestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_connectTimeout = TimeSpan.FromSeconds(10);

    private readonly Uri _serverUrl;
    private readonly X509Certificate2? _rootCertificate;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _downloadTimeout;

    // What DDT names its roots, so a refused certificate from one can be told apart from an administrator's.
    private const string DdtRootSubjectPrefix = "CN=DDT root ";

    // How the server's certificate fared against the pinned root in the latest handshake, and who issued it, so a
    // refused connection can say what to fix.
    private int _certificateErrors;
    private string? _certificateIssuer;

    private HttpClient _client;

    // The agent keeps the default timeouts; only tests shorten them.
    public HttpAgentServer(
        Uri serverUrl,
        X509Certificate2? rootCertificate,
        TimeSpan? requestTimeout = null,
        TimeSpan? connectTimeout = null,
        TimeSpan? downloadTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(serverUrl);

        _serverUrl = serverUrl;
        _rootCertificate = rootCertificate;
        _requestTimeout = requestTimeout ?? s_requestTimeout;
        _connectTimeout = connectTimeout ?? s_connectTimeout;
        _downloadTimeout = downloadTimeout ?? AgentLimits.DownloadTimeout;
        _client = NewClient();
    }

    // Closes the connections this agent keeps open to the server, before it starts a newer agent and waits for it. A
    // restart of the machine would otherwise leave them open on the server, where the next start of Windows PE, which
    // uses the same client ports, would collide with them. Later requests open new connections.
    public void CloseConnections() => Interlocked.Exchange(ref _client, NewClient()).Dispose();

    private HttpClient NewClient()
    {
        X509Certificate2? rootCertificate = _rootCertificate;

        // No proxy: Windows PE has none, and looking for one loads WinHTTP for nothing. The connect timeout covers
        // the name lookup and the TLS handshake too, and ServerConnection notes which of them it ran out in.
        SocketsHttpHandler handler = new()
        {
            UseProxy = false,
            ConnectTimeout = _connectTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = ServerConnection.ConnectAsync,
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

        // Each request gets its deadline in SendAsync instead: HttpClient's own timeout fails a request the same way the
        // connect timeout does, and the log has to say which of the two it was.
        return new HttpClient(handler) { BaseAddress = _serverUrl, Timeout = Timeout.InfiniteTimeSpan };
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

    // A download can wait in the server's queue behind a whole lab for longer than a request may take, so only its own
    // deadline bounds it.
    public async Task DownloadReleaseAsync(Stream destination, CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_downloadTimeout);

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, AgentRoutes.ReleaseBinary);
            using HttpResponseMessage response = await SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                Timeout.InfiniteTimeSpan,
                deadline.Token).ConfigureAwait(false);

            await response.Content.CopyToAsync(destination, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"the download did not finish within {Duration(_downloadTimeout)}", exception);
        }
    }

    public async Task<IReadOnlyList<AgentSequenceChoice>> GetSequencesAsync(Guid machineId, string token, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, AgentRoutes.Sequences(machineId));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        return await ReadAsync(response, AgentJsonContext.Default.IReadOnlyListAgentSequenceChoice, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AgentRun> PickSequenceAsync(Guid machineId, string token, AgentRunRequest request, CancellationToken cancellationToken)
    {
        using HttpRequestMessage message = new(HttpMethod.Post, AgentRoutes.Runs(machineId))
        {
            Content = JsonContent.Create(request, AgentJsonContext.Default.AgentRunRequest),
        };

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await SendAsync(message, cancellationToken).ConfigureAwait(false);

        return await ReadAsync(response, AgentJsonContext.Default.AgentRun, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AgentRunReportResult> ReportRunAsync(
        Guid machineId,
        string token,
        Guid runId,
        AgentRunReport report,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, AgentRoutes.RunReport(machineId, runId))
        {
            Content = JsonContent.Create(report, AgentJsonContext.Default.AgentRunReport),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        return await ReadAsync(response, AgentJsonContext.Default.AgentRunReportResult, cancellationToken).ConfigureAwait(false);
    }

    public Task<long?> HeadRunFileAsync(Guid machineId, string token, Guid runId, string sha256, CancellationToken cancellationToken) =>
        HeadAsync(AgentRoutes.RunFile(machineId, runId, sha256), token, cancellationToken);

    public Task<AgentImageStream> OpenRunFileAsync(
        Guid machineId,
        string token,
        Guid runId,
        string sha256,
        long offset,
        CancellationToken cancellationToken) =>
        OpenAsync(AgentRoutes.RunFile(machineId, runId, sha256), token, offset, cancellationToken);

    public async Task<string> GetRunUnattendAsync(Guid machineId, string token, Guid runId, Guid stepId, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, AgentRoutes.RunStepUnattend(machineId, runId, stepId));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AgentJoinDomainCredentials> GetRunJoinCredentialsAsync(
        Guid machineId,
        string token,
        Guid runId,
        Guid stepId,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, AgentRoutes.RunStepCredentials(machineId, runId, stepId));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        return await ReadAsync(response, AgentJsonContext.Default.AgentJoinDomainCredentials, cancellationToken).ConfigureAwait(false);
    }

    // A GET of the first byte rather than HEAD: an answer to HEAD has no body, so a refusal would lose the server's
    // reason. Only the headers are read, in case a server ignores the range and sends the whole file.
    private async Task<long?> HeadAsync(string route, string token, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Range = new RangeHeaderValue(0, 0);

        using HttpResponseMessage response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _requestTimeout, cancellationToken)
            .ConfigureAwait(false);

        return response.StatusCode == HttpStatusCode.PartialContent
            ? response.Content.Headers.ContentRange?.Length
            : response.Content.Headers.ContentLength;
    }

    // Without a deadline: an image takes far longer than a request may, and a stalled read is caught by the caller
    // instead.
    private async Task<AgentImageStream> OpenAsync(string route, string token, long offset, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        using HttpRequestMessage request = new(HttpMethod.Get, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (offset > 0)
        {
            request.Headers.Range = new RangeHeaderValue(offset, null);
        }

        HttpResponseMessage response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Timeout.InfiniteTimeSpan, cancellationToken)
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

    public void Dispose() => _client.Dispose();

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        SendAsync(request, HttpCompletionOption.ResponseContentRead, _requestTimeout, cancellationToken);

    // A timeout becomes a TimeoutException that says which one it was, and a connection that failed says why. A cancelled
    // token stays a cancellation.
    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        HttpCompletionOption completion,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            deadline.CancelAfter(timeout);

            try
            {
                response = await _client.SendAsync(request, completion, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"the server did not answer within {Duration(timeout)}", exception);
            }
            catch (OperationCanceledException exception) when (exception.InnerException is TimeoutException
                && !cancellationToken.IsCancellationRequested)
            {
                // SocketsHttpHandler's connect timeout, which ends the request as a cancellation with a TimeoutException in
                // it. The address is named, as a mistyped one is the likeliest cause, and so is how far the connection got.
                string server = $"{_serverUrl.Host}:{_serverUrl.Port}";
                string limit = Duration(_connectTimeout);

                throw new TimeoutException(
                    ServerConnection.DescribeTimeout(request, server, limit) ?? $"the server at {server} did not accept a connection within {limit}",
                    exception);
            }
            catch (HttpRequestException exception) when (exception.HttpRequestError == HttpRequestError.SecureConnectionError
                && CertificateProblem(request.RequestUri) is { } problem)
            {
                throw new HttpRequestException(HttpRequestError.SecureConnectionError, problem, exception);
            }
            catch (HttpRequestException exception) when (ConnectionFailure.Describe(exception, _serverUrl) is { } cause)
            {
                throw new HttpRequestException(exception.HttpRequestError, cause, exception);
            }
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

    private static string Duration(TimeSpan timeout) => timeout < TimeSpan.FromMinutes(1)
        ? string.Create(CultureInfo.InvariantCulture, $"{timeout.TotalSeconds:0.#} s")
        : string.Create(CultureInfo.InvariantCulture, $"{timeout.TotalMinutes:0.#} minutes");

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
