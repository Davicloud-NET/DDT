// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace DDT.Contracts.Messages;

// A sentence for a person, as a code from ServerMessages plus its values, so a client can show it in the person's
// language. A value is a string, a number, or a message for a sentence nested in this one.
public sealed record ServerMessage(string Code, IReadOnlyDictionary<string, object> Args)
{
    // The English text, for clients that don't translate. A code this version doesn't know is shown as is.
    [JsonIgnore]
    public string Text => ServerMessages.Find(Code) is { } template ? template.Format(Args) : Code;

    public override string ToString() => Text;

    // Reads a message back from JSON, an object with a code and its values. The values stay JsonElement.
    internal static ServerMessage? FromJson(JsonElement element)
    {
        if (!element.TryGetProperty("code", out JsonElement code) || code.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        Dictionary<string, object> arguments = new(StringComparer.Ordinal);

        if (element.TryGetProperty("args", out JsonElement values) && values.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty value in values.EnumerateObject())
            {
                arguments[value.Name] = value.Value;
            }
        }

        return new ServerMessage(code.GetString()!, arguments);
    }
}
