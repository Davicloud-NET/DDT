// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Rules;

// Mac is set for a MAC rule, twelve hex digits; Manufacturer and Model for a model rule, where a null manufacturer
// matches any and a model ending in * matches every model that starts with the text before it.
public sealed record AssignmentRuleView(
    Guid Id,
    AssignmentRuleKind Kind,
    string? Mac,
    string? Manufacturer,
    string? Model,
    Guid SequenceId,
    string SequenceName,
    string? Description,
    DateTimeOffset UpdatedUtc,
    string? UpdatedBy);
