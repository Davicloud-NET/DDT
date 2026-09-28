// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Audit;

// One row of the audit log.
public sealed record AuditEntry(
    long Id,
    DateTimeOffset OccurredUtc,
    string Action,
    AuditActorKind ActorKind,
    // A copy taken when it happened, so it outlives a renamed or deleted account.
    string? ActorName,
    Guid? ActorUserId,
    Guid? ActorMachineId,
    // The id of what the action was done to, such as a machine, a run or a package, or a domain's name.
    string? SubjectId,
    string? SourceAddress,
    string? Detail);
