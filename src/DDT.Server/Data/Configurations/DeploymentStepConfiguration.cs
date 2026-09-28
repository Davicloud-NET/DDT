// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Deployments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class DeploymentStepConfiguration : IEntityTypeConfiguration<DeploymentStep>
{
    public void Configure(EntityTypeBuilder<DeploymentStep> builder)
    {
        builder.HasKey(s => new { s.DeploymentId, s.StepId });
        builder.Property(s => s.Name).HasMaxLength(DeploymentLimits.MaxStepNameLength);
        builder.Property(s => s.Kind).HasMaxLength(32);
        builder.Property(s => s.Phase).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.State).HasConversion<string>().HasMaxLength(16);

        // A report that saves after a newer one could otherwise move a finished step back to Running.
        builder.Property(s => s.State).IsConcurrencyToken();
        builder.Property(s => s.Error).HasMaxLength(DeploymentLimits.MaxErrorLength);
        builder.Property(s => s.Branch).HasConversion<string>().HasMaxLength(8);
        builder.HasOne<Deployment>().WithMany().HasForeignKey(s => s.DeploymentId).OnDelete(DeleteBehavior.Cascade);
    }
}
