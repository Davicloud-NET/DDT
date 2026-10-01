// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class DdtUserConfiguration : IEntityTypeConfiguration<DdtUser>
{
    public void Configure(EntityTypeBuilder<DdtUser> builder)
    {
        builder.Property(u => u.Source).HasConversion<string>().HasMaxLength(16);
        builder.Property(u => u.DirectoryObjectId).HasMaxLength(256);
        builder.Property(u => u.DisplayName).HasMaxLength(256);
        builder.HasIndex(u => u.DirectoryObjectId).IsUnique();
    }
}
