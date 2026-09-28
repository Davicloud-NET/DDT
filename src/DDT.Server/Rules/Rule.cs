// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Rules;

// One rule of the ordered list, see RuleView: a rule whose When holds for a machine, or that has none, chooses the
// sequence, sets Values and gives the machine roles RoleIds, and the first rule to choose a sequence or set a value wins
// it. It never authorizes a machine. Position counts from 0 at the top and is unique.
//
// When, Values and RoleIds are JSON as DdtJsonContext writes a ConditionNode, a list of NamedValue and a list of role ids;
// When is null for a rule that matches every machine. RoleIds can name a role that was deleted since, which gives
// nothing.
public sealed class Rule
{
    public Guid Id { get; set; }

    public int Position { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public bool Enabled { get; set; }

    public string? When { get; set; }

    // Null for a rule that only sets values or gives roles.
    public Guid? TaskSequenceId { get; set; }

    public string Values { get; set; } = "[]";

    public string RoleIds { get; set; } = "[]";

    // Raised by every save and checked on it, so an editor saving over a newer save is told instead.
    public long Revision { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public string? UpdatedByName { get; set; }
}
