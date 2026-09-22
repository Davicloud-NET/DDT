// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Data;

public sealed class AuditEvent
{
    public long Id { get; set; }

    public DateTimeOffset OccurredUtc { get; set; }

    public required string Action { get; set; }

    public Guid? ActorUserId { get; set; }

    public Guid? ActorMachineId { get; set; }

    public string? ActorName { get; set; }

    public string? SubjectId { get; set; }

    public string? SourceAddress { get; set; }

    public const int MaxDetailLength = 2048;

    public string? Detail { get; set; }
}
