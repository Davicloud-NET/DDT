// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text.Json;

namespace DDT.Server.Autopilot;

// A Microsoft answer's JSON body, or nothing when it has none.
public sealed class JsonAnswer : IDisposable
{
    private readonly JsonDocument? _document;

    private JsonAnswer(JsonDocument? document) => _document = document;

    public JsonElement? Root => _document?.RootElement is { ValueKind: JsonValueKind.Object } root ? root : null;

    public static async Task<JsonAnswer> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return new JsonAnswer(body.Length == 0 ? null : JsonDocument.Parse(body));
        }
        catch (JsonException)
        {
            return new JsonAnswer(null);
        }
    }

    public string? String(string name) => String(Root, name);

    public int? Int(string name) =>
        Root is { } root && root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)
            ? number
            : null;

    // The token endpoint's error_description, whose first line is the reason; trace and correlation ids follow.
    public string TokenError() =>
        (String("error_description") ?? String("error") ?? "no reason given").Split(['\r', '\n'], 2)[0].Trim();

    // Graph's error object: {"error": {"code": ..., "message": ...}}.
    public string GraphError()
    {
        JsonElement? error = Root is { } root && root.TryGetProperty("error", out JsonElement value) && value.ValueKind == JsonValueKind.Object ? value : null;
        string? code = String(error, "code");
        string? message = String(error, "message");

        if (code is null)
        {
            return message?.Trim() ?? "no reason given";
        }

        return message is null ? code : string.Create(CultureInfo.InvariantCulture, $"{code}: {message.Trim()}");
    }

    public void Dispose() => _document?.Dispose();

    public static string? String(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out JsonElement member) && member.ValueKind == JsonValueKind.String
            ? member.GetString()
            : null;
}
