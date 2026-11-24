// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Server.Data;
using DDT.Server.Autopilot;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Records;

// Keeps the machine's latest hardware hash. The same hash again changes nothing. Another one is a new collection and
// starts its upload over.
public sealed class HardwareHashRecords(
    DdtDbContext database,
    MachineRecordsPublisher publisher,
    DdtSettings settings,
    TimeProvider timeProvider)
{
    public async Task<RecordRefusal?> SaveAsync(Machine machine, AgentHardwareHash hash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(hash);

        if (Problem(hash) is { } problem)
        {
            return new RecordRefusal(StatusCodes.Status400BadRequest, problem);
        }

        AutopilotOptions autopilot = settings.Current.Autopilot;
        string serialNumber = hash.SerialNumber.Trim();
        string? groupTag = Tag(hash.GroupTag) ?? Tag(autopilot.DefaultGroupTag);
        MachineHardwareHash? row = await database.MachineHardwareHashes
            .FirstOrDefaultAsync(h => h.MachineId == machine.Id, cancellationToken)
            .ConfigureAwait(false);

        if (row is not null && row.Hash == hash.Hash && row.SerialNumber == serialNumber && row.GroupTag == groupTag)
        {
            return null;
        }

        if (row is null)
        {
            row = new MachineHardwareHash { MachineId = machine.Id, SerialNumber = serialNumber, Hash = hash.Hash };
            database.MachineHardwareHashes.Add(row);
        }

        row.SerialNumber = serialNumber;
        row.Hash = hash.Hash;
        row.GroupTag = groupTag;
        row.CollectedUtc = timeProvider.GetUtcNow();
        row.UploadState = autopilot.CanUpload() ? HardwareHashUploadState.Pending : HardwareHashUploadState.None;
        row.UploadMessage = null;
        row.ImportId = null;
        row.UploadStartedUtc = null;

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await publisher.ChangedAsync(machine.Id, cancellationToken).ConfigureAwait(false);

        return null;
    }

    // None before the machine's hash was ever collected.
    public async Task<AgentHardwareHashStatus> StatusAsync(Guid machineId, CancellationToken cancellationToken)
    {
        MachineHardwareHash? row = await database.MachineHardwareHashes
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.MachineId == machineId, cancellationToken)
            .ConfigureAwait(false);

        return new AgentHardwareHashStatus(row?.UploadState ?? HardwareHashUploadState.None, row?.UploadMessage);
    }

    private static string? Problem(AgentHardwareHash hash)
    {
        if (string.IsNullOrWhiteSpace(hash.SerialNumber) || hash.SerialNumber.Trim().Length > RecordLimits.MaxSerialNumberLength)
        {
            return $"The serial number is empty or longer than {RecordLimits.MaxSerialNumberLength} characters.";
        }

        if (string.IsNullOrEmpty(hash.Hash) || hash.Hash.Length > RecordLimits.MaxHashLength || !IsBase64(hash.Hash))
        {
            return $"The hardware hash is not base64 of at most {RecordLimits.MaxHashLength} characters.";
        }

        return hash.GroupTag is { } tag && tag.Trim().Length > RecordLimits.MaxGroupTagLength
            ? $"The group tag is longer than {RecordLimits.MaxGroupTagLength} characters."
            : null;
    }

    private static string? Tag(string? tag) => string.IsNullOrWhiteSpace(tag) ? null : tag.Trim();

    private static bool IsBase64(string text) => System.Buffers.Text.Base64.IsValid(text);
}
