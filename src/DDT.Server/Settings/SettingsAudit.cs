// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using System.Text.Json.Nodes;
using DDT.Server.Data;

namespace DDT.Server.Settings;

// A settings row names the section and lists what changed, never a secret's value. A list longer than a row holds goes
// on in further rows.
internal static class SettingsAudit
{
    public static IEnumerable<AuditEvent> Rows(
        string action,
        string section,
        SettingsActor actor,
        DateTimeOffset now,
        string lead,
        IReadOnlyList<string> changes)
    {
        List<string> details = [];
        StringBuilder detail = new(lead);

        foreach (string change in changes)
        {
            string part = (detail.Length > lead.Length ? "; " : " ") + change;

            if (detail.Length + part.Length > AuditEvent.MaxDetailLength && detail.Length > lead.Length)
            {
                details.Add(detail.ToString());
                detail.Clear().Append(lead).Append(' ').Append(change);
            }
            else
            {
                detail.Append(part);
            }
        }

        details.Add(detail.ToString());

        return details.Select(text => new AuditEvent
        {
            OccurredUtc = now,
            Action = action,
            ActorUserId = actor.UserId,
            ActorName = actor.Name,
            SubjectId = section,
            SourceAddress = actor.Address,
            Detail = StoredText.Bound(text, AuditEvent.MaxDetailLength),
        });
    }

    // What changed between two documents, field by field. A map or a list names only the entries added, removed or
    // changed, so a large map does not fill the row with what stayed.
    public static List<string> Changes(SettingsSectionDefinition definition, JsonObject before, JsonObject after)
    {
        List<string> changes = [];

        foreach (SettingField field in definition.Fields.Where(field => !field.IsSecret))
        {
            JsonNode? old = SettingsJson.Get(before, field);
            JsonNode? now = SettingsJson.Get(after, field);

            if (SettingsJson.Same(old, now))
            {
                continue;
            }

            changes.Add(field.Kind == SettingFieldKind.Collection ? Collection(field, old, now) : $"{field.Name}: {Show(old)} to {Show(now)}");
        }

        return changes;
    }

    private static string Collection(SettingField field, JsonNode? old, JsonNode? now)
    {
        List<string> parts = [];

        if (old is JsonArray || now is JsonArray)
        {
            List<string> before = [.. (old as JsonArray ?? []).Select(Show)];
            List<string> after = [.. (now as JsonArray ?? []).Select(Show)];
            parts.AddRange(after.Except(before).Select(entry => $"added {entry}"));
            parts.AddRange(before.Except(after).Select(entry => $"removed {entry}"));
        }
        else
        {
            JsonObject before = old as JsonObject ?? [];
            JsonObject after = now as JsonObject ?? [];

            foreach ((string key, JsonNode? value) in after)
            {
                if (!before.TryGetPropertyValue(key, out JsonNode? earlier))
                {
                    parts.Add($"added '{key}' ({Show(value)})");
                }
                else if (!SettingsJson.Same(earlier, value))
                {
                    parts.Add($"changed '{key}' to {Show(value)}");
                }
            }

            parts.AddRange(before.Where(entry => !after.ContainsKey(entry.Key)).Select(entry => $"removed '{entry.Key}'"));
        }

        return $"{field.Name}: {(parts.Count == 0 ? "reordered" : string.Join(", ", parts))}";
    }

    private static string Show(JsonNode? value) => value switch
    {
        null => "unset",
        JsonValue text when text.TryGetValue(out string? s) => string.IsNullOrEmpty(s) ? "unset" : $"'{s}'",
        _ => value.ToJsonString(),
    };
}
