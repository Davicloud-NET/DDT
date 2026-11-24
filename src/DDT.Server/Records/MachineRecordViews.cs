// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;
using DDT.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Records;

// What a machine's page shows of its records. No password and no hash leaves through here.
public sealed class MachineRecordViews(DdtDbContext database)
{
    public async Task<MachineRecordsView> ReadAsync(Guid machineId, CancellationToken cancellationToken)
    {
        List<RecoveryKey> keys = await database.RecoveryKeys
            .AsNoTracking()
            .Where(k => k.MachineId == machineId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        MachineHardwareHashView? hash = await database.MachineHardwareHashes
            .AsNoTracking()
            .Where(h => h.MachineId == machineId)
            .Select(h => new MachineHardwareHashView(h.SerialNumber, h.GroupTag, h.CollectedUtc, h.UploadState, h.UploadMessage))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // SQLite can't order by DateTimeOffset, and a machine has few keys.
        return new MachineRecordsView(
            machineId,
            [
                .. keys
                    .OrderByDescending(k => k.CreatedUtc)
                    .ThenByDescending(k => k.Id)
                    .Select(k => new RecoveryKeyView(k.Id, k.ProtectorId, k.Volume, k.RunId, k.CreatedUtc, k.ReplacedUtc)),
            ],
            hash);
    }
}
