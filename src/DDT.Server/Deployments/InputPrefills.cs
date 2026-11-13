// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Core.Applications;

namespace DDT.Server.Deployments;

// What a multiple choice question starts with. A rule names applications by name, but the asked Applications input
// offers them by id, so an entry that matches a choice's label becomes that choice's value.
public static class InputPrefills
{
    public static string? Of(InputDeclaration input, string? value)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.Kind != InputKind.MultiChoice || string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        InputChoice[] choices = [.. (input.Choices ?? []).OfType<InputChoice>()];
        string[] entries = value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string[] ticked = [.. entries.Select(entry => ByLabel(choices, entry) ?? entry)];

        return ticked.SequenceEqual(entries, StringComparer.Ordinal) ? value : string.Join(';', ticked.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    // The same for the values a preview starts the inputs with.
    public static IReadOnlyList<ResolvedValue> Of(SequenceDefinition? asked, IReadOnlyList<ResolvedValue> defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);

        IReadOnlyList<InputDeclaration?> inputs = asked?.Inputs ?? [];

        return
        [
            .. defaults.Select(value =>
                inputs.FirstOrDefault(input => input is not null && string.Equals(input.Name, value.Name, StringComparison.OrdinalIgnoreCase)) is { } input
                    ? value with { Value = Of(input, value.Value) }
                    : value),
        ];
    }

    // A value always wins over a label.
    private static string? ByLabel(InputChoice[] choices, string entry)
    {
        if (choices.Any(choice => string.Equals(choice.Value, entry, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        string? name = ApplicationNames.Normalize(entry);

        return choices.FirstOrDefault(choice => choice.Label is { } label && ApplicationNames.Normalize(label) == name)?.Value;
    }
}
