// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace DDT.Server.Data;

// Writes down that a database already has the tables of the first migration, for databases that got them another way:
// a SQLite file from before SQLite was migrated, and a PostgreSQL database from the migrations that became one.
internal static class MigrationHistory
{
    public static string First(DdtDbContext context) => context.Database.GetMigrations().First();

    public static Task StartAtFirstAsync(DdtDbContext context, IReadOnlyList<string> replaced, CancellationToken cancellationToken)
    {
        IHistoryRepository history = context.GetService<IHistoryRepository>();
        HistoryRow first = new(First(context), ProductInfo.GetVersion());

        List<string> statements = [history.GetCreateIfNotExistsScript()];
        statements.AddRange(replaced.Select(history.GetDeleteScript));
        statements.Add(history.GetInsertScript(first));

        // One transaction, so a start that is cut short leaves the history as it was
        return context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                foreach (string statement in statements)
                {
                    // EF Core wrote these, and ExecuteSqlRaw reads braces as parameters
                    string literal = statement.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);
                    await context.Database.ExecuteSqlRawAsync(literal, cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        });
    }
}
