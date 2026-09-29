// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.Property(a => a.Name).HasMaxLength(AccountLimits.MaxNameLength);
        builder.Property(a => a.NormalizedName).HasMaxLength(AccountLimits.MaxNameLength);
        builder.Property(a => a.UserName).HasMaxLength(AccountLimits.MaxUserNameLength);
        builder.Property(a => a.Domain).HasMaxLength(AccountLimits.MaxDomainLength);
        builder.Property(a => a.Revision).IsConcurrencyToken();
        builder.Property(a => a.UpdatedByName).HasMaxLength(256);
        builder.HasIndex(a => a.NormalizedName).IsUnique();
        builder.HasOne<DdtUser>().WithMany().HasForeignKey(a => a.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
