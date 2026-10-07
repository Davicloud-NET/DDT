// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Text.Json;

namespace DDT.Agent;

// Turns a failed answer into the exception the agent acts on, with the server's problem details.
internal static class ServerRefusal
{
    private const int MaxErrorDetailLength = 300;

    // Disposes the response it throws for.
    public static async Task ThrowIfFailedAsync(HttpResponseMessage response, Uri? requestUri, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();

            throw new AgentTokenRejectedException($"The server refused the token for {requestUri}.");
        }

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        HttpStatusCode status = response.StatusCode;

        // Otherwise a refusal such as a validation problem would be retried forever with no reason on the console.
        // The console is all an operator standing at the machine can see.
        string detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        response.Dispose();

        (string? title, IReadOnlyDictionary<string, string>? fieldErrors) = Problem(detail);

        if (detail.Length > MaxErrorDetailLength)
        {
            detail = detail[..MaxErrorDetailLength];
        }

        throw new AgentRequestException($"The server answered {(int)status} {status} for {requestUri}: {detail}", title, status, fieldErrors);
    }

    // Reads the title of the server's problem details, and the first message for each field of a validation problem.
    private static (string? Title, IReadOnlyDictionary<string, string>? FieldErrors) Problem(string body)
    {
        if (body.Length == 0)
        {
            return (null, null);
        }

        try
        {
            using JsonDocument problem = JsonDocument.Parse(body);
            JsonElement root = problem.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            string? title = root.TryGetProperty("title", out JsonElement found) && found.ValueKind == JsonValueKind.String ? found.GetString() : null;

            return (title, FieldErrors(root));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static Dictionary<string, string>? FieldErrors(JsonElement root)
    {
        if (!root.TryGetProperty("errors", out JsonElement errors) || errors.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        Dictionary<string, string>? fieldErrors = null;

        foreach (JsonProperty field in errors.EnumerateObject())
        {
            if (field.Value.ValueKind == JsonValueKind.Array
                && field.Value.EnumerateArray().FirstOrDefault(message => message.ValueKind == JsonValueKind.String) is { ValueKind: JsonValueKind.String } first)
            {
                fieldErrors ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                fieldErrors[field.Name] = first.GetString()!;
            }
        }

        return fieldErrors;
    }
}
