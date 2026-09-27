// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using DDT.Contracts.Deployments;
using DDT.Contracts.Messages;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using DDT.Server.Sequences;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using DeploymentStep = DDT.Server.Deployments.DeploymentStep;

namespace DDT.Server.Endpoints;

public static class DeploymentEndpoints
{
    public const int DefaultHistoryPage = 50;

    public const int MaxHistoryPage = 200;

    public static RouteGroupBuilder MapDeploymentEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // The runs of every machine, for the run history.
        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Viewer);

        // The assign dialog asks for a computer name only when the machine joins a domain, and says what the
        // assignment does to a waiting machine. The settings themselves stay on the server: they hold the
        // deployment passwords.
        group.MapGet("/options", ReadOptions).RequireAuthorization(DdtPolicies.Viewer);

        // Signs in to the domain with the join account's password, so only those who manage sequences may.
        group.MapPost("/domain-check", CheckDomainJoinAsync).RequireAuthorization(DdtPolicies.Administrator);

        // Viewers read the definition a run was given, scripts included, as they read the sequences.
        group.MapGet("/{id:guid}", ReadAsync).RequireAuthorization(DdtPolicies.Viewer);

        return group;
    }

    // Newest first, a page at a time. Run ids are version 7 GUIDs minted from the time the run was created, so their order
    // is the order of creation, down to the millisecond and then by the id, which also makes the cursor stable. SQLite
    // cannot order by the DateTimeOffset itself. The counts feed the state tabs above the list, so they leave the state
    // out, and a page further down needs none.
    private static async Task<Results<Ok<RunHistoryPage>, ValidationProblem>> ListAsync(
        DeploymentState[]? state,
        Guid? sequenceId,
        Guid? machineId,
        string? query,
        string? before,
        int? limit,
        DdtDbContext database,
        CancellationToken cancellationToken)
    {
        Guid? cursor = null;

        if (!string.IsNullOrEmpty(before))
        {
            if (!Guid.TryParseExact(before, "N", out Guid parsed))
            {
                return ServerProblems.Validation("before", ServerMessages.DeploymentHistoryCursor.With());
            }

            cursor = parsed;
        }

        int take = Math.Clamp(limit ?? DefaultHistoryPage, 1, MaxHistoryPage);
        IQueryable<Deployment> deployments = database.Deployments.AsNoTracking();

        if (sequenceId is { } sequence)
        {
            deployments = deployments.Where(d => d.TaskSequenceId == sequence);
        }

        if (machineId is { } machine)
        {
            deployments = deployments.Where(d => d.MachineId == machine);
        }

        IQueryable<HistoryRow> rows = deployments.Join(
            database.Machines.AsNoTracking(),
            run => run.MachineId,
            machine => machine.Id,
            (run, machine) => new HistoryRow { Run = run, Machine = machine });

        if (!string.IsNullOrWhiteSpace(query))
        {
            rows = Matching(rows, query.Trim());
        }

        RunStateCounts? counts = null;

        if (cursor is null)
        {
            var perState = await rows
                .GroupBy(row => row.Run.State)
                .Select(runs => new { State = runs.Key, Count = runs.Count() })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            int Count(DeploymentState counted) => perState.FirstOrDefault(c => c.State == counted)?.Count ?? 0;

            counts = new RunStateCounts(
                Count(DeploymentState.Assigned),
                Count(DeploymentState.Running),
                Count(DeploymentState.Done),
                Count(DeploymentState.Failed),
                Count(DeploymentState.Cancelled));
        }

        if (state is { Length: > 0 })
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

        return TypedResults.Ok(new RunHistoryPage(items, page.Count > take ? items[^1].Run.Id.ToString("N") : null, counts));
    }

    // Case-insensitive, in the machine's name, model and serial number and in the run's title. A MAC address is stored as
    // twelve upper case hex digits, so one typed with separators, or part of one, is compared without them.
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

    // Three booleans and the time, from one snapshot, and nothing else of the settings: Viewers read this.
    private static Ok<DeploymentOptionsView> ReadOptions(DdtSettings settings, TimeProvider timeProvider)
    {
        SettingsSnapshot snapshot = settings.Current;

        return TypedResults.Ok(new DeploymentOptionsView(
            !string.IsNullOrWhiteSpace(snapshot.Deployment.Domain.Name),
            snapshot.Machines.RequireWebApproval,
            snapshot.Machines.ZeroTouchEnabled,
            timeProvider.GetUtcNow()));
    }

    private static async Task<Ok<DomainJoinCheckView>> CheckDomainJoinAsync(
        DomainJoinCheckRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        DomainJoinCheck check,
        DdtDbContext database,
        CancellationToken cancellationToken)
    {
        DomainJoinCheckView result = await check.RunAsync(request.OrganizationalUnit, cancellationToken).ConfigureAwait(false);
        DomainJoinFinding last = result.Findings[^1];

        database.AuditEvents.Add(new AuditEvent
        {
            OccurredUtc = result.CheckedUtc,
            Action = AuditActions.DomainJoinChecked,
            ActorUserId = Principals.UserId(user),
            ActorName = user.Identity?.Name,
            SubjectId = result.Domain,
            SourceAddress = context.Connection.RemoteIpAddress?.ToString(),
            Detail = $"Checked whether {result.UserName ?? "the join account"} can join {result.Domain ?? "a domain"}" +
                (result.Controller is null ? "" : $" at {result.Controller}") + $": {(result.CanJoin ? "it can" : "it cannot")}. {last.Text}",
        });

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<DeploymentView>, NotFound>> ReadAsync(
        Guid id,
        DdtDbContext database,
        CancellationToken cancellationToken)
    {
        Deployment? run = await database.Deployments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken).ConfigureAwait(false);

        if (run is null)
        {
            return TypedResults.NotFound();
        }

        DeploymentSnapshot? snapshot = await database.DeploymentSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.DeploymentId == id, cancellationToken)
            .ConfigureAwait(false);

        List<DeploymentStep> steps = await database.DeploymentSteps
            .AsNoTracking()
            .Where(s => s.DeploymentId == id)
            .OrderBy(s => s.Index)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<DeploymentArtifact> artifacts = await database.DeploymentArtifacts
            .AsNoTracking()
            .Where(a => a.DeploymentId == id)
            .OrderBy(a => a.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new DeploymentView(
            DeploymentSummaries.From(run),
            run.MachineId,
            run.SequenceRevision,
            run.RuleId,
            snapshot is null ? null : SequenceDocuments.Read(snapshot.Definition),
            [.. steps.Select(DeploymentSummaries.Step)],
            [.. artifacts.Select(DeploymentSummaries.Artifact)],
            run.AllowSecureBootMismatch));
    }

    // A class rather than an anonymous type, so the query can be passed to Matching and filtered further.
    private sealed class HistoryRow
    {
        public required Deployment Run { get; init; }

        public required Machine Machine { get; init; }
    }
}
