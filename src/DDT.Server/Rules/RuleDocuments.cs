// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;

namespace DDT.Server.Rules;

// A rule's When, Values and RoleIds, and a machine role's Values, are stored as the contract types written by
// DdtJsonContext.
public static class RuleDocuments
{
    public static string? WriteWhen(ConditionNode? when) =>
        when is null ? null : JsonSerializer.Serialize(when, DdtJsonContext.Default.ConditionNode);

    // Returns false for a condition written by another build that no longer parses. Such a condition never holds.
    public static bool TryReadWhen(string? when, out ConditionNode? condition)
    {
        condition = null;

        if (when is null)
        {
            return true;
        }

        try
        {
            condition = JsonSerializer.Deserialize(when, DdtJsonContext.Default.ConditionNode);

            return condition is not null;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    public static string WriteValues(IReadOnlyList<NamedValue> values) =>
        JsonSerializer.Serialize(values, DdtJsonContext.Default.IReadOnlyListNamedValue);

    public static IReadOnlyList<NamedValue> ReadValues(string values) =>
        Read(values, DdtJsonContext.Default.IReadOnlyListNamedValue)?.OfType<NamedValue>().ToArray() ?? [];

    // Names are trimmed, the way templates use them. A value's text is kept as written, minus any NUL, which
    // PostgreSQL text can't hold. RuleChecks.ValuesBound refuses a missing value.
    public static IReadOnlyList<NamedValue> Clean(IReadOnlyList<NamedValue>? values) =>
    [
        .. (values ?? []).Select(value => new NamedValue(
            value.Name.Replace("\0", string.Empty, StringComparison.Ordinal).Trim(),
            value.Value.Replace("\0", string.Empty, StringComparison.Ordinal))),
    ];

    public static string WriteRoleIds(IReadOnlyList<Guid> roleIds) =>
        JsonSerializer.Serialize(roleIds, DdtJsonContext.Default.IReadOnlyListGuid);

    public static IReadOnlyList<Guid> ReadRoleIds(string roleIds) => Read(roleIds, DdtJsonContext.Default.IReadOnlyListGuid) ?? [];

    private static T? Read<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(json, type);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
