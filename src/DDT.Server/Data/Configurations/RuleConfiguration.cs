// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Rules;
using DDT.Server.Sequences;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class RuleConfiguration : IEntityTypeConfiguration<Rule>
{
    public void Configure(EntityTypeBuilder<Rule> builder)
    {
        builder.Property(r => r.Name).HasMaxLength(RuleLimits.MaxNameLength);
        builder.Property(r => r.Description).HasMaxLength(RuleLimits.MaxDescriptionLength);
        builder.Property(r => r.Revision).IsConcurrencyToken();
        builder.Property(r => r.UpdatedByName).HasMaxLength(256);

        // Two rules never share a position. The database checks the index for each row as it changes. So a reorder
        // that swaps positions first moves the changed rules out of the way, in a separate save.
        builder.HasIndex(r => r.Position).IsUnique();

        // A sequence that a rule chooses can't be deleted, so no rule is left pointing at nothing.
        builder.HasOne<TaskSequence>().WithMany().HasForeignKey(r => r.TaskSequenceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DdtUser>().WithMany().HasForeignKey(r => r.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
