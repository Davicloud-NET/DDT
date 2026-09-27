// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace DDT.Contracts.Messages;

// A sentence the server says to a person, as a stable code from ServerMessages and the values its text names, so a
// client can say it in the person's language. A value is a string, a number, or a message for a sentence within this
// one. Text is the English, which the fields beside a code carry for clients that do not translate; a code this
// version does not know has its code as its text.
public sealed record ServerMessage(string Code, IReadOnlyDictionary<string, object> Args)
{
    [JsonIgnore]
    public string Text => ServerMessages.Find(Code) is { } template ? template.Format(Args) : Code;

    public override string ToString() => Text;

    // A message as JSON gives it back: an object with a code and its values, which stay JsonElement.
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
