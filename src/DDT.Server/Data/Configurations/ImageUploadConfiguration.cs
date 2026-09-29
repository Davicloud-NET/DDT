// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;
using DDT.Server.Images;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class ImageUploadConfiguration : IEntityTypeConfiguration<ImageUpload>
{
    public void Configure(EntityTypeBuilder<ImageUpload> builder)
    {
        builder.Property(u => u.FileName).HasMaxLength(ImageUploadLimits.MaxFileNameLength);
        builder.Property(u => u.CompletedSha256).HasMaxLength(64);

        // The default fills the column for uploads made before packages existed, which were all images.
        builder.Property(u => u.Kind).HasConversion<string>().HasMaxLength(16).HasDefaultValue(UploadKind.Image);
        builder.HasIndex(u => new { u.FileName, u.Length, u.LastModified });
        builder.HasOne<DdtUser>().WithMany().HasForeignKey(u => u.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
