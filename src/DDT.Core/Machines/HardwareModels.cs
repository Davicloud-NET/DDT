// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Contracts.Messages;

namespace DDT.Core.Machines;

// Firmware's manufacturer and model names differ in case and spacing within one vendor, so they compare cleaned and in
// upper case, and a board maker's placeholder never matches. Driver packages, rules and ConditionEvaluator all use it.
public static class HardwareModels
{
    public const int MaxLength = 128;

    public const char Wildcard = '*';

    // Shorter prefixes, such as "20*" on a Lenovo, would match unrelated models.
    public const int MinPrefixLength = 3;

    // The server also drops these from the facts an agent registers with, where the other defaults of AMI and ASUS
    // firmware turn up: the system's version, serial number and SKU, and the enclosure's asset tag.
    private static readonly HashSet<string> s_placeholders = new(StringComparer.Ordinal)
    {
        "TO BE FILLED BY O.E.M.",
        "SYSTEM PRODUCT NAME",
        "SYSTEM MANUFACTURER",
        "DEFAULT STRING",
        "NOT APPLICABLE",
        "NOT SPECIFIED",
        "NONE",
        "SYSTEM VERSION",
        "SYSTEM SERIAL NUMBER",
        "SYSTEM SKU",
        "SKU",
        "TYPE1PRODUCTCONFIGID",
        "CHASSIS ASSET TAG",
        "NO ASSET TAG",
        "ASSET-1234567890",
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
