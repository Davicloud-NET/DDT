// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Rules;

// If a rule's When holds for a machine, or is null, the rule chooses the sequence, sets Values and gives the roles in
// RoleIds. The first rule to choose a sequence or set a value wins. No rule authorizes a machine. When, Values and
// RoleIds are JSON written by DdtJsonContext, and a deleted role in RoleIds gives nothing. Position counts from 0 at
// the top.
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

    // Every save checks it and raises it. An editor who saves over a newer save is told, instead of overwriting it.
    public long Revision { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public string? UpdatedByName { get; set; }
}
