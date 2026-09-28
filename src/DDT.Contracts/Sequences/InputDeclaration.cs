// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// Something asked before the run starts, on the web when a run is assigned or approved, at the machine after the pick,
// or both. Name is the variable its answer sets; an Account input sets none, its answer is kept for the one run only and
// steps name it in an AccountReference. Choices are for Choice and MultiChoice; a MultiChoice answer is its values
// separated by semicolons. MaxLength bounds a Text answer.
public sealed record InputDeclaration
{
    public required string Name { get; init; }

    public required string Label { get; init; }

    public string? Help { get; init; }

    public InputKind Kind { get; init; }

    public IReadOnlyList<InputChoice> Choices { get; init; } = [];

    public string? Default { get; init; }

    public bool Required { get; init; }

    public int? MaxLength { get; init; }

    public InputAsk AskAt { get; init; }

    // Only for an Account input: where the account may be used, as for a stored account.
    public AccountDestination? Account { get; init; }
}

public enum InputKind
{
    Text,
    Choice,
    MultiChoice,
    YesNo,

    // A user name and a password.
    Account,
}

// Both first: a member the JSON leaves out reads as the first value, and an input without AskAt is asked in both places.
public enum InputAsk
{
    Both,
    Web,
    Machine,
}

// Label is what the person sees, Value what the answer sets; a null label shows the value.
public sealed record InputChoice(string Value, string? Label = null);

// Domain is the domain the account joins, Hosts the share hosts it may connect to, and RunAs whether a script may run as
// it. A password given for one destination is never sent to another.
public sealed record AccountDestination
{
    public string? Domain { get; init; }

    public IReadOnlyList<string> Hosts { get; init; } = [];

    public bool RunAs { get; init; }
}
