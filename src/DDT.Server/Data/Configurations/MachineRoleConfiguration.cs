// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class MachineRoleConfiguration : IEntityTypeConfiguration<MachineRole>
{
    public void Configure(EntityTypeBuilder<MachineRole> builder)
    {
        builder.Property(r => r.Name).HasMaxLength(RuleLimits.MaxRoleNameLength);
        builder.Property(r => r.NormalizedName).HasMaxLength(RuleLimits.MaxRoleNameLength);
        builder.Property(r => r.Description).HasMaxLength(RuleLimits.MaxDescriptionLength);
        builder.Property(r => r.Revision).IsConcurrencyToken();
        builder.Property(r => r.UpdatedByName).HasMaxLength(256);
        builder.HasIndex(r => r.NormalizedName).IsUnique();
        builder.HasOne<DdtUser>().WithMany().HasForeignKey(r => r.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
