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

    // Sorted newest first by id, which is the order the rows were stored in. That also lets the id work as the cursor.
    private static async Task<Results<Ok<AuditPage>, ValidationProblem>> ListAsync(
        [AsParameters] AuditQuery query,
        DdtDbContext database,
        CancellationToken cancellationToken)
    {
        if (query.From is { } start && query.To is { } end && end <= start)
        {
            return ServerProblems.Validation("to", ServerMessages.AuditRangeEnd.With());
        }

        int take = Math.Clamp(query.Limit ?? DefaultPage, 1, MaxPage);
        List<AuditEvent> page = await query.Filter(database.AuditEvents.AsNoTracking())
            .OrderByDescending(e => e.Id)
            .Take(take + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<AuditEntry> items = [.. page.Take(take).Select(AuditEntries.From)];

        return TypedResults.Ok(new AuditPage(items, page.Count > take ? items[^1].Id : null));
    }
}
