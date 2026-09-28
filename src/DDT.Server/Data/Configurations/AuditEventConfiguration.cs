// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DDT.Server.Data.Configurations;

internal sealed class AuditEventConfiguration(bool isSqlite) : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.Property(a => a.Action).HasMaxLength(64);
        builder.Property(a => a.ActorName).HasMaxLength(256);
        builder.Property(a => a.SubjectId).HasMaxLength(64);
        builder.Property(a => a.SourceAddress).HasMaxLength(64);
        builder.Property(a => a.Detail).HasMaxLength(AuditEvent.MaxDetailLength);
        builder.HasIndex(a => a.OccurredUtc);
        builder.HasIndex(a => a.Action);

        // SQLite has no DateTimeOffset type and only compares it for equality, but the audit log is filtered by time.
        // UTC ticks compare in order and keep every digit. EF Core's own binary converter doesn't.
        if (isSqlite)
        {
            builder.Property(a => a.OccurredUtc).HasConversion(new ValueConverter<DateTimeOffset, long>(
                time => time.UtcTicks,
                ticks => new DateTimeOffset(ticks, TimeSpan.Zero)));
        }
    }
}
