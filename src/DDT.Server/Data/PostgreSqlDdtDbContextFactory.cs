// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DDT.Server.Data;

public sealed class PostgreSqlDdtDbContextFactory : IDesignTimeDbContextFactory<PostgreSqlDdtDbContext>
{
    public PostgreSqlDdtDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<PostgreSqlDdtDbContext> options = DesignTimeOptions.For<PostgreSqlDdtDbContext>();
        options.UseDdtPostgreSql(DesignTimeOptions.Connection("Host=localhost;Database=ddt;Username=ddt;Password=ddt"));

        return new PostgreSqlDdtDbContext(options.Options);
    }
}
