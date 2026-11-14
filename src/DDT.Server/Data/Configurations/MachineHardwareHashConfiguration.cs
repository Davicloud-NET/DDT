// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Machines;
using DDT.Server.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class MachineHardwareHashConfiguration : IEntityTypeConfiguration<MachineHardwareHash>
{
    public void Configure(EntityTypeBuilder<MachineHardwareHash> builder)
    {
        builder.HasKey(h => h.MachineId);
        builder.Property(h => h.SerialNumber).HasMaxLength(RecordLimits.MaxSerialNumberLength);
        builder.Property(h => h.Hash).HasMaxLength(RecordLimits.MaxHashLength);
        builder.Property(h => h.GroupTag).HasMaxLength(RecordLimits.MaxGroupTagLength);
        builder.Property(h => h.UploadState).HasConversion<string>().HasMaxLength(16);
        builder.Property(h => h.UploadMessage).HasMaxLength(RecordLimits.MaxUploadMessageLength);
        builder.Property(h => h.ImportId).HasMaxLength(RecordLimits.MaxImportIdLength);
        builder.HasOne<Machine>().WithMany().HasForeignKey(h => h.MachineId).OnDelete(DeleteBehavior.Cascade);
    }
}
