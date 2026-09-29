// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Sequences;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class TaskSequenceConfiguration : IEntityTypeConfiguration<TaskSequence>
{
    public void Configure(EntityTypeBuilder<TaskSequence> builder)
    {
        builder.Property(s => s.Name).HasMaxLength(SequenceLimits.MaxNameLength);
        builder.Property(s => s.NormalizedName).HasMaxLength(SequenceLimits.MaxNameLength);
        builder.Property(s => s.Description).HasMaxLength(SequenceLimits.MaxDescriptionLength);
        builder.Property(s => s.Revision).IsConcurrencyToken();
        builder.Property(s => s.UpdatedByName).HasMaxLength(256);
        builder.HasIndex(s => s.NormalizedName).IsUnique();
        builder.HasOne<DdtUser>().WithMany().HasForeignKey(s => s.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
