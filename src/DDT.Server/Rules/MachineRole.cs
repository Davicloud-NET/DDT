// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Rules;

// A named set of values that rules give machines, such as "Kiosk" (see MachineRoleView). It isn't a user role. Values
// is a list of NamedValue, as JSON written by DdtJsonContext.
public sealed class MachineRole
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    // The trimmed name in upper case. It's unique, so two role names can't differ only in case.
    public required string NormalizedName { get; set; }

    public string? Description { get; set; }

    public string Values { get; set; } = "[]";

    // Every save checks it and raises it. An editor who saves over a newer save is told, instead of overwriting it.
    public long Revision { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public string? UpdatedByName { get; set; }
}
