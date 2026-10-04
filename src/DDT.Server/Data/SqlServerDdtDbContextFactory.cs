// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DDT.Server.Data;

public sealed class SqlServerDdtDbContextFactory : IDesignTimeDbContextFactory<SqlServerDdtDbContext>
{
    public SqlServerDdtDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<SqlServerDdtDbContext> options = DesignTimeOptions.For<SqlServerDdtDbContext>();
        options.UseDdtSqlServer(DesignTimeOptions.Connection("Server=localhost;Database=ddt;Integrated Security=true;TrustServerCertificate=true"));

        return new SqlServerDdtDbContext(options.Options);
    }
}
