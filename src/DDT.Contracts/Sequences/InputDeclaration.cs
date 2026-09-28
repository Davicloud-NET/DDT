// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// A question asked before the run starts. It's asked on the web when a run is assigned or approved, at the machine
// after the pick, or both.
public sealed record InputDeclaration
{
    // The variable its answer sets. An Account input sets no variable. Its answer is only kept for that one run, and
    // steps name it in an AccountReference.
    public required string Name { get; init; }

    public required string Label { get; init; }

    public string? Help { get; init; }

    public InputKind Kind { get; init; }

    // For Choice and MultiChoice. A MultiChoice answer is its values separated by semicolons.
    public IReadOnlyList<InputChoice> Choices { get; init; } = [];

    public string? Default { get; init; }

    public bool Required { get; init; }

    // The maximum length of a Text answer.
    public int? MaxLength { get; init; }

    public InputAsk AskAt { get; init; }

    // Only for an Account input. Where the account may be used, the same as for a stored account.
    public AccountDestination? Account { get; init; }
}
