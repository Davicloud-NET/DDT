// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Text;

namespace DDT.Server.Tests.Autopilot;

// Answers in place of Microsoft Entra ID and Microsoft Graph, so no request leaves the test. Each request takes the next
// scripted answer and is kept with its body; one without an answer fails the test.
public sealed class MicrosoftHandler : HttpMessageHandler, IHttpClientFactory
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _answers = new();

    public List<(HttpMethod Method, Uri Uri, string? Authorization, string Body)> Requests { get; } = [];

    public MicrosoftHandler Then(HttpStatusCode status, string body)
    {
        _answers.Enqueue((status, body));

        return this;
    }

    public MicrosoftHandler Token(string token = "token-1", int expiresIn = 3599) =>
        Then(HttpStatusCode.OK, $$"""{"token_type":"Bearer","expires_in":{{expiresIn}},"access_token":"{{token}}"}""");

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));

        if (!_answers.TryDequeue(out (HttpStatusCode Status, string Body) answer))
        {
            throw new InvalidOperationException($"No scripted answer for {request.Method} {request.RequestUri}.");
        }

        return new HttpResponseMessage(answer.Status) { Content = new StringContent(answer.Body, Encoding.UTF8, "application/json") };
    }
}
