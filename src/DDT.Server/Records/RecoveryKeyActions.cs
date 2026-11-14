// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Server.Data;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Records;

// What an administrator does with a recovery key on the web. Each reveal and each deletion is an audit row that names
// the key ID, never the password.
public sealed class RecoveryKeyActions(
    DdtDbContext database,
    RecoveryKeyProtector protector,
    MachineRecordsPublisher publisher,
    TimeProvider timeProvider)
{
    // Null if the machine has no such key. The audit row is saved before the password leaves.
    public async Task<(string? Password, ServerMessage? Refusal)> RevealAsync(Guid machineId, Guid keyId, Actor actor, CancellationToken cancellationToken)
    {
        if (await FindAsync(machineId, keyId, cancellationToken).ConfigureAwait(false) is not { } key)
        {
            return (null, null);
        }

        if (protector.Unprotect(machineId, key.ProtectorId, key.ProtectedPassword) is not { } password)
        {
            return (null, ServerMessages.RecoveryKeyUnreadable.With());
        }

        Audit(AuditActions.RecoveryKeyRevealed, key, actor);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return (password, null);
    }

    // Found is false if the machine has no such key. Only a replaced key can go, because the current one may be the
    // only copy.
    public async Task<(bool Found, ServerMessage? Refusal)> DeleteAsync(Guid machineId, Guid keyId, Actor actor, CancellationToken cancellationToken)
    {
        if (await FindAsync(machineId, keyId, cancellationToken).ConfigureAwait(false) is not { } key)
        {
            return (false, null);
        }

        if (key.ReplacedUtc is null)
        {
            return (true, ServerMessages.RecoveryKeyCurrent.With());
        }

        database.RecoveryKeys.Remove(key);
        Audit(AuditActions.RecoveryKeyDeleted, key, actor);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await publisher.ChangedAsync(machineId, cancellationToken).ConfigureAwait(false);

        return (true, null);
    }

    private Task<RecoveryKey?> FindAsync(Guid machineId, Guid keyId, CancellationToken cancellationToken) =>
        database.RecoveryKeys.FirstOrDefaultAsync(k => k.Id == keyId && k.MachineId == machineId, cancellationToken);

    private void Audit(string action, RecoveryKey key, Actor actor) =>
        database.AuditEvents.Add(AuditEvents.Create(
            action,
            key.MachineId.ToString("D"),
            actor,
            timeProvider.GetUtcNow(),
            $"Key ID {RecoveryKeyIds.Shown(key.ProtectorId)} of {key.Volume}."));
}
