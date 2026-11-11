// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Deployments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class DeploymentApplicationPlanConfiguration : IEntityTypeConfiguration<DeploymentApplicationPlan>
{
    public void Configure(EntityTypeBuilder<DeploymentApplicationPlan> builder)
    {
        builder.HasKey(p => new { p.DeploymentId, p.StepId });
        builder.HasOne<Deployment>().WithMany().HasForeignKey(p => p.DeploymentId).OnDelete(DeleteBehavior.Cascade);
    }
}
