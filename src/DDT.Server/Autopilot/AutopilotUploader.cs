// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;
using DDT.Server.Data;
using DDT.Server.Records;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Autopilot;

// Uploads Pending hardware hashes while the Autopilot settings are complete, and follows each import until Autopilot
// says it is done or failed. An import still running after GiveUpAfter fails. Never logs a hash or the secret.
public sealed partial class AutopilotUploader(
    IServiceScopeFactory scopes,
    DdtSettings settings,
    IAutopilotDirectory directory,
    TimeProvider timeProvider,
    ILogger<AutopilotUploader> logger)
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(30);

    public const string TookTooLong = "Autopilot had not finished the import after 30 minutes. Upload it again from the machine's page.";

    // Returns how many rows changed. A network failure ends the pass; the next one tries again.
    public async Task<int> UploadOnceAsync(CancellationToken cancellationToken)
    {
        AutopilotOptions account = settings.Current.Autopilot;

        if (!account.CanUpload())
        {
            return 0;
        }

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        MachineRecordsPublisher publisher = scope.ServiceProvider.GetRequiredService<MachineRecordsPublisher>();
        List<MachineHardwareHash> pending = await database.MachineHardwareHashes
            .Where(h => h.UploadState == HardwareHashUploadState.Pending)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        int changed = 0;

        foreach (MachineHardwareHash row in pending)
        {
            string hash = row.Hash;

            try
            {
                if (!await AdvanceAsync(account, row, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                LogUnreachable(exception.Message);

                break;
            }

            // A new collection may have replaced the row while Autopilot answered; that one starts its own upload.
            if (!await database.MachineHardwareHashes.AnyAsync(h => h.MachineId == row.MachineId && h.Hash == hash, cancellationToken).ConfigureAwait(false))
            {
                database.Entry(row).State = EntityState.Detached;

                continue;
            }

            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await publisher.ChangedAsync(row.MachineId, cancellationToken).ConfigureAwait(false);
            changed++;
        }

        return changed;
    }

    // True when the row changed.
    private async Task<bool> AdvanceAsync(AutopilotOptions account, MachineHardwareHash row, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        try
        {
            if (row.ImportId is null)
            {
                AutopilotImport started = await directory.ImportAsync(account, new AutopilotDevice(row.SerialNumber, row.Hash, row.GroupTag), cancellationToken)
                    .ConfigureAwait(false);
                row.ImportId = started.Id;
                row.UploadStartedUtc = now;
                Apply(row, started);
                LogImportStarted(row.MachineId, started.Id);

                return true;
            }

            if (now - (row.UploadStartedUtc ?? now) > GiveUpAfter)
            {
                Fail(row, TookTooLong);

                return true;
            }

            return Apply(row, await directory.ReadAsync(account, row.ImportId, cancellationToken).ConfigureAwait(false));
        }
        catch (AutopilotDirectoryException exception)
        {
            Fail(row, exception.Message);

            return true;
        }
    }

    private bool Apply(MachineHardwareHash row, AutopilotImport import)
    {
        switch (import.Status)
        {
            case AutopilotImportStatus.Complete:
                row.UploadState = HardwareHashUploadState.Imported;
                row.UploadMessage = null;
                LogImported(row.MachineId);

                return true;
            case AutopilotImportStatus.Error:
                Fail(row, import.Error ?? "Autopilot gave no reason.");

                return true;
            default:
                return false;
        }
    }

    private void Fail(MachineHardwareHash row, string message)
    {
        row.UploadState = HardwareHashUploadState.Failed;
        row.UploadMessage = message.Length > RecordLimits.MaxUploadMessageLength ? message[..RecordLimits.MaxUploadMessageLength] : message;
        LogFailed(row.MachineId, row.UploadMessage);
    }

    [LoggerMessage(EventId = 450, Level = LogLevel.Information, Message = "Sent the hardware hash of machine {MachineId} to Autopilot as import {ImportId}")]
    private partial void LogImportStarted(Guid machineId, string importId);

    [LoggerMessage(EventId = 451, Level = LogLevel.Information, Message = "Autopilot imported the hardware hash of machine {MachineId}")]
    private partial void LogImported(Guid machineId);

    [LoggerMessage(EventId = 452, Level = LogLevel.Warning, Message = "Autopilot did not import the hardware hash of machine {MachineId}: {Reason}")]
    private partial void LogFailed(Guid machineId, string reason);

    [LoggerMessage(EventId = 453, Level = LogLevel.Warning, Message = "Could not reach Microsoft for the Autopilot uploads, trying again later: {Reason}")]
    private partial void LogUnreachable(string reason);
}
