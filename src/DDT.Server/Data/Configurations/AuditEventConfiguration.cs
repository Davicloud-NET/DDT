// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
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
    }
}
