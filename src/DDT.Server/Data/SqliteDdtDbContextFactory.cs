// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DDT.Server.Data;

public sealed class SqliteDdtDbContextFactory : IDesignTimeDbContextFactory<SqliteDdtDbContext>
{
    public SqliteDdtDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<SqliteDdtDbContext> options = DesignTimeOptions.For<SqliteDdtDbContext>();
        options.UseDdtSqlite(DesignTimeOptions.Connection("ddt-design-time.db"));

        return new SqliteDdtDbContext(options.Options);
    }
}
