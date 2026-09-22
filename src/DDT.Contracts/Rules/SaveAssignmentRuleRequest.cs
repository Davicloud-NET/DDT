// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Rules;

// Only the fields of the rule's kind are read: Mac for a MAC rule, Manufacturer and Model for a model rule.
public sealed record SaveAssignmentRuleRequest(
    AssignmentRuleKind Kind,
    string? Mac,
    string? Manufacturer,
    string? Model,
    Guid SequenceId,
    string? Description);
