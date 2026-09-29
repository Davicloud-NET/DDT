// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Server.Data;
using DDT.Server.Deployments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DDT.Server.Endpoints;

// The machine's page and the machine can answer at the same moment, and nothing else on the run's row tells the two
// apart. So the answers are first compared and replaced in one statement, and whoever answered in the meantime wins.
public static class RunAnswerSaves
{
    // True if saved, along with everything else the request changed. The changes are only accepted once the transaction
    // commits, so the execution strategy can run it again.
    public static async Task<bool> SaveAsync(DdtDbContext database, Deployment run, string? before, CancellationToken cancellationToken)
    {
        string? after = run.Answers;
        IExecutionStrategy strategy = database.Database.CreateExecutionStrategy();
        bool saved = await strategy.ExecuteAsync(
            async cancellation =>
            {
                await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellation).ConfigureAwait(false);

                int claimed = await database.Deployments
                    .Where(d => d.Id == run.Id && d.State == DeploymentState.Assigned && d.Answers == before)
                    .ExecuteUpdateAsync(set => set.SetProperty(d => d.Answers, after), cancellation)
                    .ConfigureAwait(false);

                if (claimed == 0)
                {
                    await transaction.RollbackAsync(cancellation).ConfigureAwait(false);

                    return false;
                }

                try
                {
                    await database.SaveChangesAsync(acceptAllChangesOnSuccess: false, cancellation).ConfigureAwait(false);
                }
                catch (DbUpdateException)
                {
                    // A concurrency token, or an account given for the same input at the same moment.
                    await transaction.RollbackAsync(cancellation).ConfigureAwait(false);

                    return false;
                }

                await transaction.CommitAsync(cancellation).ConfigureAwait(false);

                return true;
            },
            cancellationToken).ConfigureAwait(false);

        if (saved)
        {
            database.ChangeTracker.AcceptAllChanges();
        }

        return saved;
    }
}
