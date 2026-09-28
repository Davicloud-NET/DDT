// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Images;
using DDT.Server.Packages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class PackageConfiguration : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> builder)
    {
        builder.Property(p => p.Name).HasMaxLength(PackageLimits.MaxNameLength);
        builder.Property(p => p.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.Sha256).HasMaxLength(64);
        builder.Property(p => p.Description).HasMaxLength(PackageLimits.MaxDescriptionLength);
        builder.Property(p => p.OriginalFileName).HasMaxLength(ImageUploadLimits.MaxFileNameLength);
        builder.Property(p => p.UploadedByName).HasMaxLength(256);
        builder.HasIndex(p => p.Sha256);
        builder.HasOne<DdtUser>().WithMany().HasForeignKey(p => p.UploadedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
