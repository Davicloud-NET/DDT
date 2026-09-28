// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class MachineConfiguration : IEntityTypeConfiguration<Machine>
{
    public void Configure(EntityTypeBuilder<Machine> builder)
    {
        builder.Property(m => m.SmbiosUuid).HasMaxLength(64);
        builder.Property(m => m.PrimaryMac).HasMaxLength(32);
        builder.Property(m => m.MacAddresses).HasMaxLength(256);
        builder.Property(m => m.AgentVersion).HasMaxLength(32);
        builder.Property(m => m.LastSeenAddress).HasMaxLength(64);
        builder.Property(m => m.Manufacturer).HasMaxLength(128);
        builder.Property(m => m.Model).HasMaxLength(128);
        builder.Property(m => m.SerialNumber).HasMaxLength(128);
        builder.Property(m => m.AssignedName).HasMaxLength(15);
        builder.Property(m => m.FirstSeenAddress).HasMaxLength(64);
        builder.Property(m => m.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(m => m.AgentEnvironment).HasConversion<string>().HasMaxLength(16).HasDefaultValue(AgentEnvironment.WindowsPE);
        builder.Property(m => m.TrustedUefiCas).HasConversion<string>().HasMaxLength(32);

        // State and generation are checked on save, so an approval, a rejection and a registration that
        // starts over cannot silently overwrite one another: the loser retries or reports a conflict.
        builder.Property(m => m.State).IsConcurrencyToken();
        builder.Property(m => m.TokenGeneration).IsConcurrencyToken();
        builder.Property(m => m.ActiveDeploymentId).IsConcurrencyToken();
        builder.Property(m => m.Disks).HasMaxLength(RegistrationValidator.MaxDisksLength);
        builder.HasIndex(m => m.SmbiosUuid);
        builder.HasIndex(m => m.PrimaryMac);
        builder.HasIndex(m => m.FirstSeenAddress);
        builder.HasOne(m => m.ApprovedBy).WithMany().HasForeignKey(m => m.ApprovedByUserId).OnDelete(DeleteBehavior.SetNull);
        builder.Property(m => m.SignedInUserName).HasMaxLength(256);
        builder.HasOne<DdtUser>().WithMany().HasForeignKey(m => m.SignedInByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
