// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization.Metadata;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;

namespace DDT.Agent;

public sealed class HttpAgentServer : IAgentServer, IDisposable
{
    private static readonly TimeSpan s_requestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_connectTimeout = TimeSpan.FromSeconds(10);

    private readonly Uri _serverUrl;
    private readonly PinnedRootTls? _tls;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _downloadTimeout;

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
        _tls = rootCertificate is null ? null : new PinnedRootTls(rootCertificate);
        _requestTimeout = requestTimeout ?? s_requestTimeout;
        _connectTimeout = connectTimeout ?? s_connectTimeout;
        _downloadTimeout = downloadTimeout ?? AgentLimits.DownloadTimeout;
        _client = NewClient();
    }

    // Called before the agent starts a newer one. WinPE's next start reuses the client ports, which would collide with
    // connections a restart left open on the server. Later requests open new connections.
    public void CloseConnections() => Interlocked.Exchange(ref _client, NewClient()).Dispose();

    public Task<AgentRegistrationResult> RegisterAsync(AgentRegistration registration, CancellationToken cancellationToken) =>
        PostAsync(
            AgentRoutes.Register,
            null,
            JsonContent.Create(registration, AgentJsonContext.Default.AgentRegistration),
            AgentJsonContext.Default.AgentRegistrationResult,
            cancellationToken);

    public Task<AgentNextResult> NextAsync(Guid machineId, string token, CancellationToken cancellationToken) =>
        GetAsync(AgentRoutes.Next(machineId), token, AgentJsonContext.Default.AgentNextResult, cancellationToken);

    public async Task SendLogAsync(Guid machineId, string token, AgentLogBatch batch, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = Request(HttpMethod.Post, AgentRoutes.Log(machineId), token);
        request.Content = JsonContent.Create(batch, AgentJsonContext.Default.AgentLogBatch);

        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public Task<AgentSignInResult> SignInAsync(Guid machineId, string token, AgentSignInRequest request, CancellationToken cancellationToken) =>
        PostAsync(
            AgentRoutes.SignIn(machineId),
            token,
            JsonContent.Create(request, AgentJsonContext.Default.AgentSignInRequest),
            AgentJsonContext.Default.AgentSignInResult,
            cancellationToken);

    public Task<AgentRelease?> GetReleaseAsync(CancellationToken cancellationToken) =>
        GetUnlessMissingAsync(AgentRoutes.Release, AgentJsonContext.Default.AgentRelease, cancellationToken);

    public Task DownloadReleaseAsync(Stream destination, CancellationToken cancellationToken) =>
        DownloadAsync(AgentRoutes.ReleaseBinary, destination, cancellationToken);

    public Task<ConsoleRelease?> GetConsoleReleaseAsync(CancellationToken cancellationToken) =>
        GetUnlessMissingAsync(AgentRoutes.ConsoleRelease, AgentJsonContext.Default.ConsoleRelease, cancellationToken);

    public Task DownloadConsoleFileAsync(string name, Stream destination, CancellationToken cancellationToken) =>
        DownloadAsync(AgentRoutes.ConsoleReleaseFile(name), destination, cancellationToken);

    public Task DownloadConsoleLogoAsync(Stream destination, CancellationToken cancellationToken) =>
        DownloadAsync(AgentRoutes.ConsoleLogo, destination, cancellationToken);

    public Task<IReadOnlyList<AgentSequenceChoice>> GetSequencesAsync(Guid machineId, string token, CancellationToken cancellationToken) =>
        GetAsync(AgentRoutes.Sequences(machineId), token, AgentJsonContext.Default.IReadOnlyListAgentSequenceChoice, cancellationToken);

    public Task<AgentRun> PickSequenceAsync(Guid machineId, string token, AgentRunRequest request, CancellationToken cancellationToken) =>
        PostAsync(
            AgentRoutes.Runs(machineId),
            token,
            JsonContent.Create(request, AgentJsonContext.Default.AgentRunRequest),
            AgentJsonContext.Default.AgentRun,
            cancellationToken);

    public Task<AgentRunReportResult> ReportRunAsync(Guid machineId, string token, Guid runId, AgentRunReport report, CancellationToken cancellationToken) =>
        PostAsync(
            AgentRoutes.RunReport(machineId, runId),
            token,
            JsonContent.Create(report, AgentJsonContext.Default.AgentRunReport),
            AgentJsonContext.Default.AgentRunReportResult,
            cancellationToken);

    public Task<AgentAnswersResult> AnswerRunInputsAsync(
        Guid machineId,
        string token,
        Guid runId,
        AgentInputAnswers answers,
        CancellationToken cancellationToken) =>
        PostAsync(
            AgentRoutes.RunAnswers(machineId, runId),
            token,
            JsonContent.Create(answers, AgentJsonContext.Default.AgentInputAnswers),
            AgentJsonContext.Default.AgentAnswersResult,
            cancellationToken);

    // Sends a GET for the first byte instead of a HEAD. An answer to HEAD has no body, so a refusal would lose the
    // server's reason. Only the headers are read, in case a server ignores the range and sends the whole file.
    public async Task<long?> HeadRunFileAsync(Guid machineId, string token, Guid runId, string sha256, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = Request(HttpMethod.Get, AgentRoutes.RunFile(machineId, runId, sha256), token);
        request.Headers.Range = new RangeHeaderValue(0, 0);

        using HttpResponseMessage response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _requestTimeout, cancellationToken)
            .ConfigureAwait(false);

        return response.StatusCode == HttpStatusCode.PartialContent
            ? response.Content.Headers.ContentRange?.Length
            : response.Content.Headers.ContentLength;
    }

    // No deadline here. An image takes far longer than a request may, and the caller catches a stalled read.
    public async Task<AgentImageStream> OpenRunFileAsync(Guid machineId, string token, RunFileRange file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentOutOfRangeException.ThrowIfNegative(file.Offset);

        using HttpRequestMessage request = Request(HttpMethod.Get, AgentRoutes.RunFile(machineId, file.RunId, file.Sha256), token);

        if (file.Offset > 0)
        {
            request.Headers.Range = new RangeHeaderValue(file.Offset, null);
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

    public async Task<string> GetRunUnattendAsync(Guid machineId, string token, Guid runId, Guid stepId, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = Request(HttpMethod.Get, AgentRoutes.RunStepUnattend(machineId, runId, stepId), token);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<AgentJoinDomainCredentials> GetRunJoinCredentialsAsync(
        Guid machineId,
        string token,
        Guid runId,
        Guid stepId,
        CancellationToken cancellationToken) =>
        GetAsync(AgentRoutes.RunStepCredentials(machineId, runId, stepId), token, AgentJsonContext.Default.AgentJoinDomainCredentials, cancellationToken);

    public Task<AgentStepAccounts> GetRunStepAccountsAsync(
        Guid machineId,
        string token,
        Guid runId,
        Guid stepId,
        CancellationToken cancellationToken) =>
        GetAsync(AgentRoutes.RunStepAccounts(machineId, runId, stepId), token, AgentJsonContext.Default.AgentStepAccounts, cancellationToken);

    public void Dispose() => _client.Dispose();

    private HttpClient NewClient()
    {
        // No proxy. WinPE has none, and looking for one loads WinHTTP for nothing. The connect timeout covers the name
        // lookup and the TLS handshake too. ServerConnection notes which of them ran out of time.
        SocketsHttpHandler handler = new()
        {
            UseProxy = false,
            ConnectTimeout = _connectTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = ServerConnection.ConnectAsync,
        };

        _tls?.Apply(handler.SslOptions);

        // SendAsync gives each request its deadline instead. HttpClient's own timeout fails a request the same way the
        // connect timeout does, and the log has to say which of the two it was.
        return new HttpClient(handler) { BaseAddress = _serverUrl, Timeout = Timeout.InfiniteTimeSpan };
    }

    private async Task<T> GetAsync<T>(string route, string token, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = Request(HttpMethod.Get, route, token);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        return await ReadAsync(response, typeInfo, cancellationToken).ConfigureAwait(false);
    }

    // Returns null when the server answers 404, which it does when it offers no such release.
    private async Task<T?> GetUnlessMissingAsync<T>(string route, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
        where T : class
    {
        using HttpRequestMessage request = Request(HttpMethod.Get, route, token: null);

        try
        {
            using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

            return await ReadAsync(response, typeInfo, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<T> PostAsync<T>(string route, string? token, HttpContent content, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = Request(HttpMethod.Post, route, token);
        request.Content = content;

        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        return await ReadAsync(response, typeInfo, cancellationToken).ConfigureAwait(false);
    }

    // A download can wait in the server's queue behind a whole lab for longer than a request may take. So only the
    // download timeout limits it.
    private async Task DownloadAsync(string route, Stream destination, CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_downloadTimeout);

        try
        {
            using HttpRequestMessage request = Request(HttpMethod.Get, route, token: null);
            using HttpResponseMessage response = await SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                Timeout.InfiniteTimeSpan,
                deadline.Token).ConfigureAwait(false);

            await response.Content.CopyToAsync(destination, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new ServerTimeoutException(
                ConnectionStage.Answer,
                $"the download did not finish within {Durations.Describe(_downloadTimeout)}",
                exception);
        }
    }

    private static HttpRequestMessage Request(HttpMethod method, string route, string? token)
    {
        HttpRequestMessage request = new(method, route);

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        SendAsync(request, HttpCompletionOption.ResponseContentRead, _requestTimeout, cancellationToken);

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        HttpCompletionOption completion,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await SendWithDeadlineAsync(request, completion, timeout, cancellationToken).ConfigureAwait(false);
        await ServerRefusal.ThrowIfFailedAsync(response, request.RequestUri, cancellationToken).ConfigureAwait(false);

        return response;
    }

    // A timeout becomes a ServerTimeoutException that says which timeout it was. A failed connection says why it
    // failed. A cancelled token stays a cancellation.
    private async Task<HttpResponseMessage> SendWithDeadlineAsync(
        HttpRequestMessage request,
        HttpCompletionOption completion,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            return await _client.SendAsync(request, completion, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new ServerTimeoutException(
                ConnectionStage.Answer,
                $"the server did not answer within {Durations.Describe(timeout)}",
                exception);
        }
        catch (OperationCanceledException exception) when (exception.InnerException is TimeoutException
            && !cancellationToken.IsCancellationRequested)
        {
            // SocketsHttpHandler's connect timeout. The message names the address, because a typo there is the
            // likeliest cause. It also says how far the connection got.
            string server = $"{_serverUrl.Host}:{_serverUrl.Port}";
            string limit = Durations.Describe(_connectTimeout);

            throw new ServerTimeoutException(
                ServerConnection.TimedOutStage(request) ?? ConnectionStage.Connection,
                ServerConnection.DescribeTimeout(request, server, limit) ?? $"the server at {server} did not accept a connection within {limit}",
                exception);
        }
        catch (HttpRequestException exception) when (exception.HttpRequestError == HttpRequestError.SecureConnectionError
            && _tls?.Problem(request.RequestUri) is { } problem)
        {
            throw new HttpRequestException(HttpRequestError.SecureConnectionError, problem, exception);
        }
        catch (HttpRequestException exception) when (ConnectionFailure.Describe(exception, _serverUrl) is { } cause)
        {
            throw new HttpRequestException(exception.HttpRequestError, cause, exception);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken).ConfigureAwait(false)
        ?? throw new HttpRequestException($"The server sent an empty answer for {response.RequestMessage?.RequestUri}.");
}
