// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Accounts;
using DDT.Server.Deployments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class RunCredentialConfiguration : IEntityTypeConfiguration<RunCredential>
{
    public void Configure(EntityTypeBuilder<RunCredential> builder)
    {
        builder.HasKey(c => new { c.DeploymentId, c.InputName });
        builder.Property(c => c.InputName).HasMaxLength(RunCredential.MaxInputNameLength);
        builder.Property(c => c.UserName).HasMaxLength(AccountLimits.MaxUserNameLength);
        builder.Property(c => c.Domain).HasMaxLength(AccountLimits.MaxDomainLength);
        builder.Property(c => c.ProvidedByName).HasMaxLength(256);

        // A run's credentials go with it, though RunCredentialCleanup deletes them long before, when the run ends.
        builder.HasOne<Deployment>().WithMany().HasForeignKey(c => c.DeploymentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<DdtUser>().WithMany().HasForeignKey(c => c.ProvidedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
