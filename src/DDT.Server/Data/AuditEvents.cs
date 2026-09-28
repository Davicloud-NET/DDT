// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Data;

public static class AuditEvents
{
    // A detail often holds what someone typed, NUL included, and PostgreSQL refuses NUL. StoredText bounds each one.
    public static AuditEvent Create(string action, string? subjectId, Actor actor, DateTimeOffset now, string? detail)
    {
        ArgumentNullException.ThrowIfNull(actor);

        return new AuditEvent
        {
            OccurredUtc = now,
            Action = action,
            ActorUserId = actor.UserId,
            ActorMachineId = actor.MachineId,
            ActorName = actor.Name,
            SubjectId = subjectId,
            SourceAddress = actor.Address,
            Detail = StoredText.Bound(detail, AuditEvent.MaxDetailLength),
        };
    }
}
