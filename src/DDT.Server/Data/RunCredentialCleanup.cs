// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Server.Deployments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DDT.Server.Data;

// A password given for one run is kept only while that run is assigned or running. Every save that moves a run out of
// those states deletes the run's credentials in the same save, so every way a run ends is covered without a call site
// having to remember it, including ways added later, and a save that fails keeps them with the run it did not end.
// RunCredentialSweeper removes, at the start, whatever a run that ended some other way left.
public sealed class RunCredentialCleanup : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is { } context && Ended(context) is { Count: > 0 } ended)
        {
            Remove(context, ended, [.. context.Set<RunCredential>().Where(c => ended.Contains(c.DeploymentId))]);
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is { } context && Ended(context) is { Count: > 0 } ended)
        {
            List<RunCredential> stored = await context.Set<RunCredential>()
                .Where(c => ended.Contains(c.DeploymentId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            Remove(context, ended, stored);
        }

        return result;
    }

    private static bool Keeps(DeploymentState state) => state is DeploymentState.Assigned or DeploymentState.Running;

    // The runs this save moves out of Assigned or Running. Entries finds the changes the save is about to make.
    private static List<Guid> Ended(DbContext context) =>
    [
        .. context.ChangeTracker.Entries<Deployment>()
            .Where(entry => entry.State == EntityState.Modified
                && Keeps(entry.Property(d => d.State).OriginalValue)
                && !Keeps(entry.Entity.State))
            .Select(entry => entry.Entity.Id),
    ];

    // A credential added in this same save was never stored, so it is dropped rather than deleted.
    private static void Remove(DbContext context, List<Guid> ended, List<RunCredential> stored)
    {
        List<EntityEntry<RunCredential>> added =
        [
            .. context.ChangeTracker.Entries<RunCredential>()
                .Where(entry => entry.State == EntityState.Added && ended.Contains(entry.Entity.DeploymentId)),
        ];

        foreach (EntityEntry<RunCredential> entry in added)
        {
            entry.State = EntityState.Detached;
        }

        context.Set<RunCredential>().RemoveRange(stored);
    }
}
