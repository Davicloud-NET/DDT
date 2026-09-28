// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class SettingsSectionConfiguration : IEntityTypeConfiguration<SettingsSection>
{
    public void Configure(EntityTypeBuilder<SettingsSection> builder)
    {
        builder.HasKey(s => s.Section);
        builder.Property(s => s.Section).HasMaxLength(SettingsSection.MaxNameLength);

        // Every save checks the version it read, so two saves of one section never overwrite each other unnoticed.
        builder.Property(s => s.Version).IsConcurrencyToken();
        builder.Property(s => s.UpdatedByName).HasMaxLength(256);
        builder.HasOne<DdtUser>().WithMany().HasForeignKey(s => s.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
