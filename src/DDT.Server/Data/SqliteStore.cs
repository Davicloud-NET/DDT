// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Data;

// The SQLite file in the store. Builds before SQLite was migrated created it from the model as ddt-dev.db and stamped
// it with a fingerprint of its tables.
internal static class SqliteStore
{
    public const string FileName = "ddt.db";

    private const string OldFileName = "ddt-dev.db";

    // The fingerprint of the last tables made that way, which are the tables of the first migration
    private const int LastCreatedSchema = 1008346332;

    public static string PathIn(string storePath) => Path.Combine(storePath, FileName);

    // Before the first connection, which would make a new file. The main file moves last: until then a start that was
    // cut short does the rest.
    public static void TakeOverOldFile(string storePath)
    {
        string old = Path.Combine(storePath, OldFileName);

        if (File.Exists(PathIn(storePath)) || !File.Exists(old))
        {
            return;
        }

        foreach (string part in new[] { "-wal", "-shm", string.Empty })
        {
            if (File.Exists(old + part))
            {
                File.Move(old + part, PathIn(storePath) + part, overwrite: true);
            }
        }
    }

    // Returns whether it took a file from before the migrations into them.
    public static async Task<bool> AdoptAsync(DdtDbContext context, CancellationToken cancellationToken)
    {
        string file = context.Database.GetDbConnection().DataSource;

        // A query would create the file, and EF Core only turns the write-ahead log on in a file it makes itself
        if (!File.Exists(file) || (await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).Any())
        {
            return false;
        }

        if (await ScalarAsync(context, $"SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'AspNetUsers'", cancellationToken).ConfigureAwait(false) == 0)
        {
            return false;
        }

        if (await ScalarAsync(context, $"SELECT user_version AS Value FROM pragma_user_version", cancellationToken).ConfigureAwait(false) != LastCreatedSchema)
        {
            throw new InvalidOperationException(
                $"The database {file} was created by a development build whose tables DDT can't bring up to date. " +
                "Delete the file and start again; its accounts and machines are lost, and a new administrator password is made.");
        }

        await MigrationHistory.StartAtFirstAsync(context, [], cancellationToken).ConfigureAwait(false);

        return true;
    }

    private static async Task<int> ScalarAsync(DdtDbContext context, FormattableString query, CancellationToken cancellationToken) =>
        (await context.Database.SqlQuery<int>(query).ToListAsync(cancellationToken).ConfigureAwait(false)).Single();
}
