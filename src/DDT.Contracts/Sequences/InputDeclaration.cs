// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// Something asked before the run starts: on the web when a run is assigned or approved, at the machine after the
// pick, or both.
public sealed record InputDeclaration
{
    // The variable its answer sets. An Account input sets none: its answer is kept for the one run only, and steps name
    // it in an AccountReference.
    public required string Name { get; init; }

    public required string Label { get; init; }

    public string? Help { get; init; }

    public InputKind Kind { get; init; }

    // For Choice and MultiChoice; a MultiChoice answer is its values separated by semicolons.
    public IReadOnlyList<InputChoice> Choices { get; init; } = [];

    public string? Default { get; init; }

    public bool Required { get; init; }

    // Bounds a Text answer.
    public int? MaxLength { get; init; }

    public InputAsk AskAt { get; init; }

    // Only for an Account input: where the account may be used, as for a stored account.
    public AccountDestination? Account { get; init; }
}
