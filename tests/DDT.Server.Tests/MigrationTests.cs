// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

public sealed class MigrationTests
{
    // Every other test runs on SQLite through EnsureCreated. So only this one notices a model change that nobody
    // generated a migration for. Such a change would stop the first start on PostgreSQL. The test compares the model
    // with the snapshot and needs no database.
    [Fact]
    public void TheMigrationsMatchTheModel()
    {
        using DdtDbContext context = new DdtDbContextFactory().CreateDbContext([]);

        Assert.False(context.Database.HasPendingModelChanges());
    }
}
