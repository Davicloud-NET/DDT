// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Values;

namespace DDT.Contracts.Rules;

// A machine role, such as "Kiosk" or "Finance laptop": values that rules give machines together. Not a user role, which
// says what a person may do.
public sealed record MachineRoleView(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<NamedValue> Values,
    long Revision,
    int RuleCount,
    DateTimeOffset UpdatedUtc,
    string? UpdatedBy);
