// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Rules;

namespace DDT.Server.Rules;

// Chooses the sequence for the machines it matches. It never authorizes one: the machine still needs an approval on
// the web, where the rule's sequence then runs, or a sign-in at it, where the sequence is offered.
public sealed class AssignmentRule
{
    public Guid Id { get; set; }

    public AssignmentRuleKind Kind { get; set; }

    // What the rule matches, normalised, unique: two rules can never match the same machines equally well.
    public required string MatchKey { get; set; }

    public string? Mac { get; set; }

    public string? Manufacturer { get; set; }

    public string? Model { get; set; }

    public Guid TaskSequenceId { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public string? UpdatedByName { get; set; }
}
