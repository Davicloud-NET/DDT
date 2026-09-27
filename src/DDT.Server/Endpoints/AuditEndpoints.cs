// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Audit;
using DDT.Contracts.Messages;
using DDT.Server.Authentication;
using DDT.Server.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Endpoints;

// The audit log names who did what from where, which is for administrators only.
public static class AuditEndpoints
{
    public const int DefaultPage = 100;

    public const int MaxPage = 500;

    public static RouteGroupBuilder MapAuditEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    // Newest first by id, the order the rows were stored in, which also makes the id the cursor. Action matches from its
    // start, so machine. finds every machine action. Actor is any part of the name, in any case. From includes its
    // moment and to does not, so consecutive ranges never list a row twice.
    private static async Task<Results<Ok<AuditPage>, ValidationProblem>> ListAsync(
        string? action,
        string? actor,
        string? subject,
        DateTimeOffset? from,
        DateTimeOffset? to,
        long? before,
        int? limit,
        DdtDbContext database,
        CancellationToken cancellationToken)
    {
        if (from is { } start && to is { } end && end <= start)
        {
            return ServerProblems.Validation("to", ServerMessages.AuditRangeEnd.With());
        }

        int take = Math.Clamp(limit ?? DefaultPage, 1, MaxPage);
        IQueryable<AuditEvent> events = database.AuditEvents.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(action))
        {
            string prefix = action.Trim().ToLowerInvariant();
            events = events.Where(e => e.Action.StartsWith(prefix));
        }

        if (!string.IsNullOrWhiteSpace(actor))
        {
            string part = actor.Trim().ToLowerInvariant();
            events = events.Where(e => e.ActorName != null && e.ActorName.ToLower().Contains(part));
        }

        if (!string.IsNullOrWhiteSpace(subject))
        {
            string id = subject.Trim();
            events = events.Where(e => e.SubjectId == id);
        }

        if (from is { } after)
        {
            events = events.Where(e => e.OccurredUtc >= after);
        }

        if (to is { } until)
        {
            events = events.Where(e => e.OccurredUtc < until);
        }

        if (before is { } last)
        {
            events = events.Where(e => e.Id < last);
        }

        List<AuditEvent> page = await events
            .OrderByDescending(e => e.Id)
            .Take(take + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<AuditEntry> items = [.. page.Take(take).Select(AuditEntries.From)];

        return TypedResults.Ok(new AuditPage(items, page.Count > take ? items[^1].Id : null));
    }
}
