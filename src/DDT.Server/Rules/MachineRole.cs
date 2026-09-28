// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Rules;

// Values that rules give machines together, such as "Kiosk", see MachineRoleView. Not a user role. Values is JSON as
// DdtJsonContext writes a list of NamedValue.
public sealed class MachineRole
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    // The trimmed name in upper case, unique, so two roles cannot differ only in case.
    public required string NormalizedName { get; set; }

    public string? Description { get; set; }

    public string Values { get; set; } = "[]";

    // Raised by every save and checked on it, so an editor saving over a newer save is told instead.
    public long Revision { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public string? UpdatedByName { get; set; }
}
