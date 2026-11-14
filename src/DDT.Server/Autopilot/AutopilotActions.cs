// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;
using DDT.Contracts.Messages;
using DDT.Server.Data;
using DDT.Server.Records;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Autopilot;

// What people do with the hashes on the web: upload one again, and export them for Intune's own import.
public sealed class AutopilotActions(DdtDbContext database, MachineRecordsPublisher publisher, DdtSettings settings)
{
    // Puts the machine's hash back in line for the uploader, as a hash collected before the settings were complete or
    // one whose import failed needs.
    public async Task<ServerMessage?> RequestUploadAsync(Guid machineId, CancellationToken cancellationToken)
    {
        if (!settings.Current.Autopilot.CanUpload())
        {
            return ServerMessages.AutopilotNotSetUp.With();
        }

        MachineHardwareHash? row = await database.MachineHardwareHashes
            .FirstOrDefaultAsync(h => h.MachineId == machineId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return ServerMessages.AutopilotNoHash.With();
        }

        row.UploadState = HardwareHashUploadState.Pending;
        row.UploadMessage = null;
        row.ImportId = null;
        row.UploadStartedUtc = null;
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await publisher.ChangedAsync(machineId, cancellationToken).ConfigureAwait(false);

        return null;
    }

    // Machines without a hash are left out.
    public async Task<(byte[]? Csv, ServerMessage? Refusal)> CsvAsync(IReadOnlyCollection<Guid> machineIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machineIds);

        if (machineIds.Count > AutopilotCsv.MaxDevices)
        {
            return (null, ServerMessages.AutopilotTooMany.With("max", AutopilotCsv.MaxDevices));
        }

        List<AutopilotDevice> devices = await database.MachineHardwareHashes
            .AsNoTracking()
            .Where(h => machineIds.Contains(h.MachineId))
            .OrderBy(h => h.SerialNumber)
            .Select(h => new AutopilotDevice(h.SerialNumber, h.Hash, h.GroupTag))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return devices.Count == 0 ? (null, ServerMessages.AutopilotNoHashes.With()) : (AutopilotCsv.Write(devices), null);
    }
}
