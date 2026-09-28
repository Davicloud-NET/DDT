// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Deployments;
using DDT.Server.Machines;
using DDT.Server.Sequences;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class DeploymentConfiguration : IEntityTypeConfiguration<Deployment>
{
    public void Configure(EntityTypeBuilder<Deployment> builder)
    {
        builder.Property(d => d.Title).HasMaxLength(256);
        builder.Property(d => d.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(d => d.State).IsConcurrencyToken();
        builder.Property(d => d.Source).HasConversion<string>().HasMaxLength(16);
        builder.Property(d => d.RequestedByName).HasMaxLength(256);
        builder.Property(d => d.CurrentStepName).HasMaxLength(DeploymentLimits.MaxStepNameLength);
        builder.Property(d => d.CurrentPhase).HasConversion<string>().HasMaxLength(16);
        builder.Property(d => d.Activity).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.Error).HasMaxLength(DeploymentLimits.MaxErrorLength);
        builder.Property(d => d.PauseMessage).HasMaxLength(DeploymentLimits.MaxPauseMessageLength);
        builder.Property(d => d.ContinuedByName).HasMaxLength(256);
        builder.HasIndex(d => d.MachineId);
        builder.HasOne<Machine>().WithMany().HasForeignKey(d => d.MachineId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TaskSequence>().WithMany().HasForeignKey(d => d.TaskSequenceId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<DdtUser>().WithMany().HasForeignKey(d => d.RequestedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
