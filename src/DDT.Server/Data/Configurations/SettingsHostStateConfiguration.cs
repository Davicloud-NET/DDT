// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class SettingsHostStateConfiguration : IEntityTypeConfiguration<SettingsHostState>
{
    public void Configure(EntityTypeBuilder<SettingsHostState> builder)
    {
        builder.HasKey(s => new { s.Host, s.Section });
        builder.Property(s => s.Host).HasMaxLength(SettingsHostState.MaxHostLength);
        builder.Property(s => s.Section).HasMaxLength(SettingsSection.MaxNameLength);
        builder.Property(s => s.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.Message).HasMaxLength(SettingsHostState.MaxMessageLength);
    }
}
