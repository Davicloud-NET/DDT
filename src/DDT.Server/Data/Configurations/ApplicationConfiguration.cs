// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Server.Applications;
using DDT.Server.Packages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class ApplicationConfiguration : IEntityTypeConfiguration<Application>
{
    public void Configure(EntityTypeBuilder<Application> builder)
    {
        builder.Property(a => a.Name).HasMaxLength(ApplicationLimits.MaxNameLength);
        builder.Property(a => a.NormalizedName).HasMaxLength(ApplicationLimits.MaxNameLength);
        builder.Property(a => a.Description).HasMaxLength(ApplicationLimits.MaxDescriptionLength);
        builder.Property(a => a.Publisher).HasMaxLength(ApplicationLimits.MaxPublisherLength);
        builder.Property(a => a.Version).HasMaxLength(ApplicationLimits.MaxVersionLength);
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.Command).HasMaxLength(ApplicationLimits.MaxCommandLength);
        builder.Property(a => a.WingetId).HasMaxLength(ApplicationLimits.MaxWingetIdLength);
        builder.Property(a => a.WingetVersion).HasMaxLength(ApplicationLimits.MaxWingetVersionLength);
        builder.Property(a => a.Revision).IsConcurrencyToken();
        builder.Property(a => a.UpdatedByName).HasMaxLength(256);
        builder.HasIndex(a => a.NormalizedName).IsUnique();

        // Files are removed with the last application that names them, never under one.
        builder.HasOne<Package>().WithMany().HasForeignKey(a => a.PackageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DdtUser>().WithMany().HasForeignKey(a => a.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
