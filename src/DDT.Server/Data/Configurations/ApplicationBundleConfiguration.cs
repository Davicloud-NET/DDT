// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Server.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class ApplicationBundleConfiguration : IEntityTypeConfiguration<ApplicationBundle>
{
    public void Configure(EntityTypeBuilder<ApplicationBundle> builder)
    {
        builder.Property(b => b.Name).HasMaxLength(ApplicationLimits.MaxNameLength);
        builder.Property(b => b.NormalizedName).HasMaxLength(ApplicationLimits.MaxNameLength);
        builder.Property(b => b.Description).HasMaxLength(ApplicationLimits.MaxDescriptionLength);
        builder.Property(b => b.Revision).IsConcurrencyToken();
        builder.Property(b => b.UpdatedByName).HasMaxLength(256);
        builder.HasIndex(b => b.NormalizedName).IsUnique();
        builder.HasOne<DdtUser>().WithMany().HasForeignKey(b => b.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
