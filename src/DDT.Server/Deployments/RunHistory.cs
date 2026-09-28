// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Server.Data;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// The runs of every machine, newest first, a page at a time. Run IDs are version 7 GUIDs made from the run's creation
// time, so their order is the creation order and a stable cursor. SQLite can't order by the DateTimeOffset itself.
internal sealed class RunHistory(DdtDbContext database)
{
    public const int DefaultPage = 50;

    public const int MaxPage = 200;

    // Null if the cursor isn't valid.
    public async Task<RunHistoryPage?> PageAsync(RunHistoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Guid? cursor = null;

        if (!string.IsNullOrEmpty(query.Before))
        {
            if (!Guid.TryParseExact(query.Before, "N", out Guid parsed))
            {
                return null;
            }

            cursor = parsed;
        }

        int take = Math.Clamp(query.Limit ?? DefaultPage, 1, MaxPage);
        IQueryable<HistoryRow> rows = Rows(query);

        // The counts feed the state tabs above the list, so they ignore the state filter. A page further down doesn't
        // need them.
        RunStateCounts? counts = cursor is null ? await CountAsync(rows, cancellationToken).ConfigureAwait(false) : null;

        if (query.State is { Length: > 0 } state)
        {
            List<DeploymentState> states = [.. state.Distinct()];
            rows = rows.Where(row => states.Contains(row.Run.State));
        }

        if (cursor is { } last)
        {
            rows = rows.Where(row => row.Run.Id.CompareTo(last) < 0);
        }

        List<HistoryRow> page = await rows
            .OrderByDescending(row => row.Run.Id)
            .Take(take + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<RunHistoryItem> items = [.. page.Take(take).Select(row => RunHistoryItems.From(row.Machine, row.Run))];

        return new RunHistoryPage(items, page.Count > take ? items[^1].Run.Id.ToString("N") : null, counts);
    }

    private static async Task<RunStateCounts> CountAsync(IQueryable<HistoryRow> rows, CancellationToken cancellationToken)
    {
        var perState = await rows
            .GroupBy(row => row.Run.State)
            .Select(runs => new { State = runs.Key, Count = runs.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int Count(DeploymentState counted) => perState.FirstOrDefault(c => c.State == counted)?.Count ?? 0;

        return new RunStateCounts(
            Count(DeploymentState.Assigned),
            Count(DeploymentState.Running),
            Count(DeploymentState.Done),
            Count(DeploymentState.Failed),
            Count(DeploymentState.Cancelled));
    }

    // Searches the machine's name, model and serial number and the run's title, ignoring case. A MAC address is stored
    // as twelve upper case hex digits, so a typed one, or part of one, is compared without separators.
    private static IQueryable<HistoryRow> Matching(IQueryable<HistoryRow> rows, string query)
    {
        string text = query.ToLowerInvariant();
        string hex = new([.. query.Where(char.IsAsciiHexDigit)]);
        string? mac = hex.Length >= 2 && query.All(c => char.IsAsciiHexDigit(c) || c is ':' or '-' or '.' or ' ')
            ? hex.ToUpperInvariant()
            : null;

        return rows.Where(row => row.Run.Title.ToLower().Contains(text)
            || (row.Machine.AssignedName != null && row.Machine.AssignedName.ToLower().Contains(text))
            || (row.Machine.Model != null && row.Machine.Model.ToLower().Contains(text))
            || (row.Machine.SerialNumber != null && row.Machine.SerialNumber.ToLower().Contains(text))
            || (mac != null && (row.Machine.PrimaryMac.Contains(mac) || row.Machine.MacAddresses.Contains(mac))));
    }

    private IQueryable<HistoryRow> Rows(RunHistoryQuery query)
    {
        IQueryable<Deployment> deployments = database.Deployments.AsNoTracking();

        if (query.SequenceId is { } sequence)
        {
            deployments = deployments.Where(d => d.TaskSequenceId == sequence);
        }

        if (query.MachineId is { } machine)
        {
            deployments = deployments.Where(d => d.MachineId == machine);
        }

        IQueryable<HistoryRow> rows = deployments.Join(
            database.Machines.AsNoTracking(),
            run => run.MachineId,
            machine => machine.Id,
            (run, machine) => new HistoryRow { Run = run, Machine = machine });

        return string.IsNullOrWhiteSpace(query.Query) ? rows : Matching(rows, query.Query.Trim());
    }

    // A class rather than an anonymous type, so the query can be passed to Matching and filtered further.
    private sealed class HistoryRow
    {
        public required Deployment Run { get; init; }

        public required Machine Machine { get; init; }
    }
}
