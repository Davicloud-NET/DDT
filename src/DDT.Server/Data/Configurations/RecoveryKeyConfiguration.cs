// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Machines;
using DDT.Server.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class RecoveryKeyConfiguration : IEntityTypeConfiguration<RecoveryKey>
{
    public void Configure(EntityTypeBuilder<RecoveryKey> builder)
    {
        builder.Property(k => k.Volume).HasMaxLength(RecordLimits.VolumeLength);
        builder.HasIndex(k => new { k.MachineId, k.ProtectorId }).IsUnique();

        // A removed machine takes its keys along. MachineRemovals asks first.
        builder.HasOne<Machine>().WithMany().HasForeignKey(k => k.MachineId).OnDelete(DeleteBehavior.Cascade);
    }
}
