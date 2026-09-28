// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Deployments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class DeploymentSnapshotConfiguration : IEntityTypeConfiguration<DeploymentSnapshot>
{
    public void Configure(EntityTypeBuilder<DeploymentSnapshot> builder)
    {
        builder.HasKey(s => s.DeploymentId);
        builder.HasOne<Deployment>().WithOne().HasForeignKey<DeploymentSnapshot>(s => s.DeploymentId).OnDelete(DeleteBehavior.Cascade);
    }
}
