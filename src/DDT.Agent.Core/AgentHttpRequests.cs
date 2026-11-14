// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;

namespace DDT.Agent;

// The agent's requests as HttpAgentServer sends them, and how it reads their answers.
internal static class AgentHttpRequests
{
    public static HttpRequestMessage Create(HttpMethod method, string route, string? token)
    {
        HttpRequestMessage request = new(method, route);

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken).ConfigureAwait(false)
        ?? throw new HttpRequestException($"The server sent an empty answer for {response.RequestMessage?.RequestUri}.");
}
