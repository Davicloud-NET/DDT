// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDT.Server.Data;

public sealed class SqlServerDdtDbContext(DbContextOptions<SqlServerDdtDbContext> options) : DdtDbContext(options)
{
    // SQL Server's default collations ignore case, which would match one token's hash to another's and one name to
    // another's. The other two databases compare text exactly, and this one makes SQL Server do so too.
    private const string ExactCollation = "Latin1_General_100_BIN2";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);

        foreach (IMutableEntityType entity in builder.Model.GetEntityTypes())
        {
            foreach (IMutableProperty text in entity.GetProperties().Where(property => property.ClrType == typeof(string)))
            {
                text.SetCollation(ExactCollation);
            }

            // SQL Server refuses SET NULL where two paths lead from a user to a table. UserReferences clears these.
            foreach (IMutableForeignKey key in entity.GetForeignKeys())
            {
                if (key.PrincipalEntityType.ClrType == typeof(DdtUser) && key.DeleteBehavior == DeleteBehavior.SetNull)
                {
                    key.DeleteBehavior = DeleteBehavior.ClientSetNull;
                }
            }
        }
    }
}
