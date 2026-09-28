// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using System.Text.RegularExpressions;
using DDT.Contracts;
using DDT.Contracts.Sequences;

namespace DDT.Server.Deployments;

// The variables of a run as its agent reports them, stored as Deployment.Variables: each by name, merged into what the
// run has, since the agent sends them only when they changed. They come from outside, so they are held to the bounds the
// agent keeps, and an Account input's name is never one: its answer is a password.
public static partial class RunVariables
{
    // As many as a sequence declares, and the values as long as an answer may be.
    public const int MaxCount = 64;

    public const int MaxValueLength = 1024;

    public static IReadOnlyDictionary<string, string>? Read(string? variables)
    {
        if (variables is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(variables, DdtJsonContext.Default.IReadOnlyDictionaryStringString);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // What the run has with the reported variables merged in by name, or null when that is what it has already. A name
    // that is not one, or names an Account input, is left out; a value loses its NULs and is cut to MaxValueLength; and a
    // new name beyond MaxCount is left out.
    public static string? Merged(string? stored, IReadOnlyDictionary<string, string>? reported, SequenceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (reported is null)
        {
            return null;
        }

        HashSet<string> accounts = new(
            (definition.Inputs ?? []).Where(input => input is { Kind: InputKind.Account }).Select(input => input.Name),
            StringComparer.OrdinalIgnoreCase);
        SortedDictionary<string, string> merged = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string name, string value) in Read(stored) ?? new Dictionary<string, string>())
        {
            merged[name] = value;
        }

        foreach ((string name, string value) in reported.OrderBy(variable => variable.Key, StringComparer.Ordinal))
        {
            if (value is null || !Name().IsMatch(name) || accounts.Contains(name) || (merged.Count >= MaxCount && !merged.ContainsKey(name)))
            {
                continue;
            }

            string kept = value.Replace("\0", string.Empty, StringComparison.Ordinal);
            merged[name] = kept.Length <= MaxValueLength ? kept : kept[..MaxValueLength];
        }

        string written = JsonSerializer.Serialize<IReadOnlyDictionary<string, string>>(merged, DdtJsonContext.Default.IReadOnlyDictionaryStringString);

        return written == stored ? null : written;
    }

    // As a sequence names a variable.
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Name();
}
