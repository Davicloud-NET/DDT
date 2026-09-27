// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Nodes;

namespace DDT.Server.Settings;

// A field's place in a document of options, by its segments: absent, present with a value, or present as null.
internal static class SettingsJson
{
    public static bool Has(JsonObject document, SettingField field) => TryGet(document, field.Segments, out _);

    public static JsonNode? Get(JsonObject document, SettingField field) =>
        TryGet(document, field.Segments, out JsonNode? value) ? value : null;

    public static bool TryGet(JsonObject document, IReadOnlyList<string> segments, out JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(segments);

        JsonObject current = document;
        value = null;

        for (int index = 0; index < segments.Count; index++)
        {
            if (!current.TryGetPropertyValue(segments[index], out JsonNode? next))
            {
                return false;
            }

            if (index == segments.Count - 1)
            {
                value = next;

                return true;
            }

            if (next is not JsonObject nested)
            {
                return false;
            }

            current = nested;
        }

        return false;
    }

    // A copy of the value goes in, so no two documents ever share a node.
    public static void Set(JsonObject document, SettingField field, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(field);

        JsonObject current = document;

        for (int index = 0; index < field.Segments.Count - 1; index++)
        {
            if (current[field.Segments[index]] is not JsonObject nested)
            {
                nested = [];
                current[field.Segments[index]] = nested;
            }

            current = nested;
        }

        current[field.Segments[^1]] = value?.DeepClone();
    }

    public static bool Same(JsonNode? left, JsonNode? right) => JsonNode.DeepEquals(left, right);
}
