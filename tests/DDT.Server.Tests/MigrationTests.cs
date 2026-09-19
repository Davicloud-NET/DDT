using DDT.Server.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

public sealed class MigrationTests
{
    // Every other test runs on SQLite through EnsureCreated, so only this notices a model change nobody generated
    // a migration for, which would stop the first start on PostgreSQL. It compares the model with the snapshot
    // and needs no database.
    [Fact]
    public void TheMigrationsMatchTheModel()
    {
        using DdtDbContext context = new DdtDbContextFactory().CreateDbContext([]);

        Assert.False(context.Database.HasPendingModelChanges());
    }
}
