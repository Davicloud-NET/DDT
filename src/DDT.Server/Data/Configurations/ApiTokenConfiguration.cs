// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDT.Server.Data.Configurations;

internal sealed class ApiTokenConfiguration : IEntityTypeConfiguration<ApiToken>
{
    public void Configure(EntityTypeBuilder<ApiToken> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(ApiTokenLimits.MaxNameLength);
        builder.Property(t => t.Role).HasMaxLength(16);
        builder.Property(t => t.SecretHash).HasMaxLength(64);
        builder.Property(t => t.Hint).HasMaxLength(ApiTokenSecrets.HintLength);
        builder.Property(t => t.LastUsedAddress).HasMaxLength(64);
        builder.Property(t => t.RevokedByName).HasMaxLength(256);

        // Every request that carries a token looks it up by the hash of what it sent.
        builder.HasIndex(t => t.SecretHash).IsUnique();
        builder.HasIndex(t => t.UserId);

        // A token acts only for its user, so deleting the account deletes its tokens.
        builder.HasOne<DdtUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<DdtUser>().WithMany().HasForeignKey(t => t.RevokedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
