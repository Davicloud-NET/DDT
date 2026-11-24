// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;
using DDT.Server.Live;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Records;

// Pushes a machine's records after a save. They hold key IDs, states and dates, never a password or a hash. As with
// LiveNotifier, the request doesn't await the push.
public sealed partial class MachineRecordsPublisher(IHubContext<LiveHub> hub, MachineRecordViews views, ILogger<MachineRecordsPublisher> logger)
{
    public async Task ChangedAsync(Guid machineId, CancellationToken cancellationToken)
    {
        MachineRecordsView records = await views.ReadAsync(machineId, cancellationToken).ConfigureAwait(false);

        _ = PushAsync(records);
    }

    private async Task PushAsync(MachineRecordsView records)
    {
        try
        {
            await hub.Clients.All.SendAsync(LiveEvents.MachineRecordsChanged, records, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // A boundary: the save stands, and a page that missed the push shows the change when it is opened again.
            LogPushFailed(records.MachineId, exception);
        }
    }

    [LoggerMessage(EventId = 433, Level = LogLevel.Warning, Message = "Could not push the records of machine {MachineId}")]
    private partial void LogPushFailed(Guid machineId, Exception exception);
}
