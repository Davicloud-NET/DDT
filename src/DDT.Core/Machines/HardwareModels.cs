// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Contracts.Messages;

namespace DDT.Core.Machines;

// Manufacturer and model names as firmware reports them differ in case and spacing between models of one vendor, so
// they are compared cleaned and in upper case. Board makers leave placeholders in unset fields, which say nothing
// about the machine and must never choose drivers or a sequence. The server matches driver packages and rules with it,
// and ConditionEvaluator compares Manufacturer and Model by the same rules.
public static class HardwareModels
{
    public const int MaxLength = 128;

    public const char Wildcard = '*';

    // Shorter prefixes, such as "20*" on a Lenovo, would match unrelated models.
    public const int MinPrefixLength = 3;

    private static readonly HashSet<string> s_placeholders = new(StringComparer.Ordinal)
    {
        "TO BE FILLED BY O.E.M.",
        "SYSTEM PRODUCT NAME",
        "SYSTEM MANUFACTURER",
        "DEFAULT STRING",
        "NOT APPLICABLE",
        "NOT SPECIFIED",
        "NONE",
    };

    // Trimmed, with every run of white space as one space; null for nothing.
    public static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        StringBuilder cleaned = new(value.Length);

        foreach (string part in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            cleaned.Append(cleaned.Length > 0 ? " " : "").Append(part);
        }

        return cleaned.ToString();
    }

    public static string? Normalize(string? value) => Clean(value)?.ToUpperInvariant();

    public static bool IsPlaceholder(string? value) => Normalize(value) is { } normalized && s_placeholders.Contains(normalized);

    // What is wrong with a manufacturer or model an administrator entered to match machines by, or null. Only a model
    // can end in the wildcard.
    public static ServerMessage? Problem(string? value, bool required, bool wildcard)
    {
        string? cleaned = Clean(value);

        if (cleaned is null)
        {
            return required ? ServerMessages.ModelEnter.With() : null;
        }

        if (cleaned.Length > MaxLength || cleaned.Any(char.IsControl))
        {
            return ServerMessages.ModelNameLength.With("max", MaxLength);
        }

        int position = cleaned.IndexOf(Wildcard, StringComparison.Ordinal);
        string shown = new(Wildcard, 1);

        if (position >= 0 && (!wildcard || position != cleaned.Length - 1))
        {
            return (wildcard ? ServerMessages.ModelWildcardLast : ServerMessages.ModelManufacturerWhole).With("wildcard", shown);
        }

        if (position >= 0 && position < MinPrefixLength)
        {
            return ServerMessages.ModelWildcardPrefix.With("min", MinPrefixLength, "wildcard", shown);
        }

        return IsPlaceholder(cleaned.TrimEnd(Wildcard)) ? ServerMessages.ModelPlaceholder.With("value", cleaned) : null;
    }

    // A null pattern matches anything. A value that is unknown or a placeholder matches no pattern.
    public static bool Matches(string? pattern, string? value)
    {
        if (Normalize(pattern) is not { } expected)
        {
            return true;
        }

        if (Normalize(value) is not { } actual || s_placeholders.Contains(actual))
        {
            return false;
        }

        return expected[^1] == Wildcard
            ? actual.StartsWith(expected[..^1], StringComparison.Ordinal)
            : actual == expected;
    }

    public static bool IsPrefix(string? pattern) => Clean(pattern) is { } cleaned && cleaned[^1] == Wildcard;
}
