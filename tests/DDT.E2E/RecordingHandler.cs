// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Concurrent;
using System.Text;

namespace DDT.E2E;

// Keeps every response body as text before the client reads it. Over long polling, that's everything the hub sends.
internal sealed class RecordingHandler(HttpMessageHandler inner, ConcurrentQueue<string> bodies) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        bodies.Enqueue(Encoding.UTF8.GetString(body));

        ByteArrayContent copy = new(body);

        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
        {
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        response.Content.Dispose();
        response.Content = copy;

        return response;
    }
}
