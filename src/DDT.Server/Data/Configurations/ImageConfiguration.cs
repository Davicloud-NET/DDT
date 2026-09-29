// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Images;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class ImageConfiguration : IEntityTypeConfiguration<Image>
{
    public void Configure(EntityTypeBuilder<Image> builder)
    {
        builder.Property(i => i.Name).HasMaxLength(256);
        builder.Property(i => i.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(i => i.Sha256).HasMaxLength(64);
        builder.Property(i => i.Edition).HasMaxLength(64);
        builder.Property(i => i.Architecture).HasMaxLength(16);
        builder.Property(i => i.Version).HasMaxLength(32);
        builder.Property(i => i.Language).HasMaxLength(16);
        builder.Property(i => i.OriginalFileName).HasMaxLength(256);
        builder.Property(i => i.UploadedByName).HasMaxLength(256);
        builder.Property(i => i.BootCapability).HasConversion<string>().HasMaxLength(16);
        builder.Property(i => i.SignedUnder).HasConversion<string>().HasMaxLength(32);
        builder.Property(i => i.BootDetail).HasMaxLength(RawImageLimits.MaxBootDetailLength);
        builder.Property(i => i.SourceSha256).HasMaxLength(64);
        builder.HasIndex(i => i.SourceSha256);
        builder.HasIndex(i => i.Sha256);
        builder.HasOne<DdtUser>().WithMany().HasForeignKey(i => i.UploadedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
