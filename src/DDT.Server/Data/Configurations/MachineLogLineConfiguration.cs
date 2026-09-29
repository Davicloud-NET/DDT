// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class MachineLogLineConfiguration : IEntityTypeConfiguration<MachineLogLine>
{
    public void Configure(EntityTypeBuilder<MachineLogLine> builder)
    {
        builder.Property(l => l.Level).HasConversion<string>().HasMaxLength(16);
        builder.Property(l => l.Message).HasMaxLength(MachineLogLimits.MaxMessageLength);
        builder.HasIndex(l => new { l.MachineId, l.Id });
        builder.HasIndex(l => new { l.DeploymentId, l.Id });
        builder.HasOne<Machine>().WithMany().HasForeignKey(l => l.MachineId).OnDelete(DeleteBehavior.Cascade);
    }
}
