// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Deployments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class DeploymentArtifactConfiguration : IEntityTypeConfiguration<DeploymentArtifact>
{
    public void Configure(EntityTypeBuilder<DeploymentArtifact> builder)
    {
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.Name).HasMaxLength(256);
        builder.Property(a => a.Sha256).HasMaxLength(64);
        builder.Property(a => a.Language).HasMaxLength(16);
        builder.Property(a => a.BootCapability).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.SignedUnder).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(a => a.Sha256);
        builder.HasIndex(a => a.SourceId);
        builder.HasOne<Deployment>().WithMany().HasForeignKey(a => a.DeploymentId).OnDelete(DeleteBehavior.Cascade);
    }
}
