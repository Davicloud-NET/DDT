// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Data.Common;
using System.Runtime.CompilerServices;
using DDT.Server.Endpoints;
using DDT.Server.Live;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DDT.Server.Data;

// Pushes every new audit row to the live connections once it's stored. That's after the save, or after its transaction
// commits, and never after a rollback. When a user acted with an API token, the row gets the token before it's stored.
public sealed class AuditInterceptor(LiveNotifier live, IHttpContextAccessor httpContextAccessor) : ISaveChangesInterceptor, IDbTransactionInterceptor
{
    private const int MaxActorNameLength = 256;

    // Holds, per context, the rows of a save in progress and the rows saved in a transaction that hasn't committed
    // yet. A pooled context serves one request at a time, and the table forgets a context once it's gone.
    private readonly ConditionalWeakTable<DbContext, List<AuditEvent>> _saving = [];
    private readonly ConditionalWeakTable<DbContext, List<AuditEvent>> _uncommitted = [];

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData);

        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Collect(eventData);

        return ValueTask.FromResult(result);
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Saved(eventData);

        return result;
    }

    public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Saved(eventData);

        return ValueTask.FromResult(result);
    }

    public void SaveChangesFailed(DbContextErrorEventData eventData) => Forget(eventData, _saving);

    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData, _saving);

        return Task.CompletedTask;
    }

    public void SaveChangesCanceled(DbContextEventData eventData) => Forget(eventData, _saving);

    public Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData, _saving);

        return Task.CompletedTask;
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Committed(eventData);

    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Committed(eventData);

        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => Forget(eventData, _uncommitted);

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData, _uncommitted);

        return Task.CompletedTask;
    }

    public void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) => Forget(eventData, _uncommitted);

    public Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData, _uncommitted);

        return Task.CompletedTask;
    }

    private void Collect(DbContextEventData eventData)
    {
        if (eventData.Context is not { } context)
        {
            return;
        }

        List<AuditEvent> added = [.. context.ChangeTracker.Entries<AuditEvent>().Where(e => e.State == EntityState.Added).Select(e => e.Entity)];

        if (added.Count > 0)
        {
            NameTokenActor(added);
            _saving.AddOrUpdate(context, added);
        }
        else
        {
            _saving.Remove(context);
        }
    }

    // Names the actor as alice (token build-server), so the log reads right without a lookup. The token's id is kept
    // next to it.
    private void NameTokenActor(List<AuditEvent> added)
    {
        if (httpContextAccessor.HttpContext?.User is not { } user
            || Principals.ApiTokenId(user) is not { } tokenId
            || Principals.UserId(user) is not { } userId)
        {
            return;
        }

        string? name = StoredText.Bound(Principals.ActorName(user), MaxActorNameLength);

        foreach (AuditEvent audit in added.Where(a => a.ActorUserId == userId && a.ActorTokenId is null))
        {
            audit.ActorTokenId = tokenId;
            audit.ActorName = name;
        }
    }

    // The ids exist only now, after the insert.
    private void Saved(DbContextEventData eventData)
    {
        if (eventData.Context is not { } context || !_saving.TryGetValue(context, out List<AuditEvent>? added))
        {
            return;
        }

        _saving.Remove(context);

        if (context.Database.CurrentTransaction is null)
        {
            Push(added);
        }
        else
        {
            _uncommitted.GetOrCreateValue(context).AddRange(added);
        }
    }

    private void Committed(DbContextEventData eventData)
    {
        if (eventData.Context is { } context && _uncommitted.TryGetValue(context, out List<AuditEvent>? added))
        {
            _uncommitted.Remove(context);
            Push(added);
        }
    }

    private static void Forget(DbContextEventData eventData, ConditionalWeakTable<DbContext, List<AuditEvent>> pending)
    {
        if (eventData.Context is { } context)
        {
            pending.Remove(context);
        }
    }

    private void Push(List<AuditEvent> added)
    {
        if (added.Count > 0)
        {
            live.AuditAppended([.. added.OrderBy(audit => audit.Id).Select(AuditEntries.From)]);
        }
    }
}
