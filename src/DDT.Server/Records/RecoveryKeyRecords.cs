// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Server.Data;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Records;

// Keeps a recovery key a step handed over, and marks the machine's older key of the volume replaced. The same key
// again changes nothing, so a step that lost the answer just sends it again.
public sealed class RecoveryKeyRecords(
    DdtDbContext database,
    RecoveryKeyProtector protector,
    MachineRecordsPublisher publisher,
    TimeProvider timeProvider)
{
    public async Task<RecordRefusal?> SaveAsync(Machine machine, Guid runId, AgentRecoveryKey key, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(key);

        if (Problem(key) is { } problem)
        {
            return new RecordRefusal(StatusCodes.Status400BadRequest, problem);
        }

        string volume = key.Volume.ToUpperInvariant();

        if (await KnownAsync(machine.Id, key.ProtectorId, cancellationToken).ConfigureAwait(false) is { } known)
        {
            return Same(known, key, volume) ? null : Conflict(key.ProtectorId);
        }

        await AddAsync(machine.Id, runId, key, actor, cancellationToken).ConfigureAwait(false);

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // The same key sent twice at once, and the other request saved it.
            database.ChangeTracker.Clear();

            if (await KnownAsync(machine.Id, key.ProtectorId, cancellationToken).ConfigureAwait(false) is not { } raced)
            {
                throw;
            }

            return Same(raced, key, volume) ? null : Conflict(key.ProtectorId);
        }

        await publisher.ChangedAsync(machine.Id, cancellationToken).ConfigureAwait(false);

        return null;
    }

    // The machine's current key of the volume becomes replaced, and the audit row names the new one.
    private async Task AddAsync(Guid machineId, Guid runId, AgentRecoveryKey key, Actor actor, CancellationToken cancellationToken)
    {
        string volume = key.Volume.ToUpperInvariant();
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<RecoveryKey> older = await database.RecoveryKeys
            .Where(k => k.MachineId == machineId && k.Volume == volume && k.ReplacedUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (RecoveryKey replaced in older)
        {
            replaced.ReplacedUtc = now;
        }

        database.RecoveryKeys.Add(new RecoveryKey
        {
            Id = Guid.CreateVersion7(now),
            MachineId = machineId,
            RunId = runId,
            ProtectorId = key.ProtectorId,
            Volume = volume,
            ProtectedPassword = protector.Protect(machineId, key.ProtectorId, key.Password),
            CreatedUtc = now,
        });
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.RecoveryKeyRecorded,
            machineId.ToString("D"),
            actor,
            now,
            $"Key ID {key.ProtectorId:D} of {volume}, from run {runId:D}."));
    }

    private static string? Problem(AgentRecoveryKey key)
    {
        if (key.ProtectorId == Guid.Empty)
        {
            return "The recovery key has no key ID.";
        }

        if (!RecoveryPasswords.IsWellFormed(key.Password))
        {
            return "The recovery password is not 8 groups of 6 digits as BitLocker makes them.";
        }

        return key.Volume is { Length: RecordLimits.VolumeLength } volume && char.IsAsciiLetter(volume[0]) && volume[1] == ':'
            ? null
            : "The volume is not a drive such as C:.";
    }

    private bool Same(RecoveryKey known, AgentRecoveryKey key, string volume) =>
        known.Volume == volume && protector.Unprotect(known.MachineId, known.ProtectorId, known.ProtectedPassword) == key.Password;

    private static RecordRefusal Conflict(Guid protectorId) =>
        new(StatusCodes.Status409Conflict, $"The server holds another recovery password or volume for key ID {protectorId:D}.");

    private Task<RecoveryKey?> KnownAsync(Guid machineId, Guid protectorId, CancellationToken cancellationToken) =>
        database.RecoveryKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.MachineId == machineId && k.ProtectorId == protectorId, cancellationToken);
}
