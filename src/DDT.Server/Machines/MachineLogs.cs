// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Server.Data;
using DDT.Server.Live;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Machines;

// A machine's log: what its agent sends, and the pages the machine's page reads.
internal sealed class MachineLogs(DdtDbContext database, LiveNotifier live, TimeProvider timeProvider)
{
    // Each line is tagged with the run that is active when it arrives. The agent sends what it logged before a report
    // that ends the run ahead of that report.
    public async Task AppendAsync(Guid machineId, AgentLogBatch batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);

        DateTimeOffset received = timeProvider.GetUtcNow();
        TimeSpan skew = MachineLogClock.Skew(batch.SentUtc, received);
        Guid? runId = await database.Machines
            .Where(m => m.Id == machineId)
            .Select(m => m.ActiveDeploymentId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        List<MachineLogLine> added = [.. (batch.Lines ?? []).Select(line => Line(machineId, runId, line, skew, received))];

        database.MachineLogLines.AddRange(added);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Keep the newest lines. The oldest are the least useful once a machine has logged this much.
        long? cutoff = await database.MachineLogLines
            .Where(l => l.MachineId == machineId)
            .OrderByDescending(l => l.Id)
            .Skip(MachineLogLimits.MaxStoredLinesPerMachine)
            .Select(l => (long?)l.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (cutoff is { } oldestKept)
        {
            await database.MachineLogLines
                .Where(l => l.MachineId == machineId && l.Id <= oldestKept)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (added.Count > 0)
        {
            live.MachineLogAppended(machineId, added.Max(l => l.Id));
        }
    }

    // Null for a machine that is gone.
    public async Task<MachineLogPage?> ReadAsync(MachineLogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await database.Machines.AnyAsync(m => m.Id == query.Id, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        int take = Math.Clamp(query.Limit ?? MachineLogLimits.DefaultLinesPerRead, 1, MachineLogLimits.MaxLinesPerRead);
        IQueryable<MachineLogLine> lines = database.MachineLogLines.AsNoTracking().Where(line => line.MachineId == query.Id);

        if (query.DeploymentId is { } runId)
        {
            lines = lines.Where(line => line.DeploymentId == runId);
        }

        IQueryable<MachineLogLine> window = lines;

        if (query.After is { } first)
        {
            window = window.Where(line => line.Id > first);
        }

        if (query.Before is { } last)
        {
            window = window.Where(line => line.Id < last);
        }

        List<MachineLogLine> page = query.After is not null
            ? await window.OrderBy(line => line.Id).Take(take).ToListAsync(cancellationToken).ConfigureAwait(false)
            : [.. (await window.OrderByDescending(line => line.Id).Take(take).ToListAsync(cancellationToken).ConfigureAwait(false)).AsEnumerable().Reverse()];

        long boundary = page.Count > 0 ? page[0].Id : query.After + 1 ?? query.Before ?? long.MaxValue;
        bool hasOlder = await lines.AnyAsync(line => line.Id < boundary, cancellationToken).ConfigureAwait(false);

        return new MachineLogPage(
            [
                .. page.Select(line => new MachineLogEntry(
                    line.Id,
                    line.TimestampUtc,
                    line.ReceivedUtc,
                    line.Level,
                    line.Message,
                    line.AgentTimestampUtc,
                    line.DeploymentId,
                    line.StepId)),
            ],
            hasOlder);
    }

    private static MachineLogLine Line(Guid machineId, Guid? runId, AgentLogLine line, TimeSpan skew, DateTimeOffset received)
    {
        // PostgreSQL text cannot hold a NUL, and a batch it refuses would be resent forever.
        string message = (line.Message ?? string.Empty).Replace("\0", string.Empty, StringComparison.Ordinal);

        return new MachineLogLine
        {
            MachineId = machineId,
            TimestampUtc = MachineLogClock.Corrected(line.TimestampUtc, skew, received),
            AgentTimestampUtc = line.TimestampUtc.ToUniversalTime(),
            ReceivedUtc = received,
            Level = Enum.IsDefined(line.Level) ? line.Level : AgentLogLevel.Information,
            Message = message.Length <= MachineLogLimits.MaxMessageLength ? message : message[..MachineLogLimits.MaxMessageLength],
            DeploymentId = runId,
            StepId = runId is null ? null : line.StepId,
        };
    }
}
