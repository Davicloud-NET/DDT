// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Sequences;

public sealed class TaskSequence
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    // The trimmed name in upper case. It's unique, so two sequence names can't differ only in case.
    public required string NormalizedName { get; set; }

    public string? Description { get; set; }

    // The SequenceDefinition as DdtJsonContext writes it. Each run keeps its own copy, so an edit never changes a run.
    public required string Definition { get; set; }

    // Every save checks it and raises it. An editor who saves over a newer save is told, instead of overwriting it.
    public long Revision { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public string? UpdatedByName { get; set; }
}
