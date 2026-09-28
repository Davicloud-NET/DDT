// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Audit;

namespace DDT.Server.Data;

public static class AuditEntries
{
    public static AuditEntry From(AuditEvent audit)
    {
        ArgumentNullException.ThrowIfNull(audit);

        return new AuditEntry(
            audit.Id,
            audit.OccurredUtc,
            audit.Action,
            KindOf(audit),
            audit.ActorName,
            audit.ActorUserId,
            audit.ActorMachineId,
            audit.SubjectId,
            audit.SourceAddress,
            audit.Detail);
    }

    // A technician who signs in at a machine is recorded with both ids. The person counts as the one who acted.
    private static AuditActorKind KindOf(AuditEvent audit) =>
        audit.ActorTokenId is not null ? AuditActorKind.Token
        : audit.ActorUserId is not null ? AuditActorKind.User
        : audit.ActorMachineId is not null ? AuditActorKind.Machine
        : AuditActorKind.System;
}
