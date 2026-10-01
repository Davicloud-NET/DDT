// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Data;

// PostgreSQL had twelve migrations before each database got one. A database at the last of them has the tables of
// the one there is now.
internal static class PostgreSqlHistory
{
    private const string LastOfTheTwelve = "20260928002610_M7FlowBuilder";

    // Returns whether it rewrote the history.
    public static async Task<bool> AdoptAsync(DdtDbContext context, CancellationToken cancellationToken)
    {
        List<string> applied = [.. await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)];

        if (applied.Count == 0 || applied.Contains(MigrationHistory.First(context), StringComparer.Ordinal))
        {
            return false;
        }

        if (!applied.Contains(LastOfTheTwelve, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"The database was last migrated to {applied[^1]} by a build from before DDT's migrations became one. " +
                $"Start a build that still has them once, so it reaches {LastOfTheTwelve}, or give DDT an empty database.");
        }

        await MigrationHistory.StartAtFirstAsync(context, applied, cancellationToken).ConfigureAwait(false);

        return true;
    }
}
