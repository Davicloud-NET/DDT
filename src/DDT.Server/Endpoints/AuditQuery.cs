// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;

namespace DDT.Server.Endpoints;

// The audit log's filters, bound from the query string. Action matches from the start, so "machine." finds every
// machine action. Actor matches any part of the name, in any case. From is inclusive and To is exclusive, so
// consecutive ranges never list a row twice.
internal sealed record AuditQuery(
    string? Action,
    string? Actor,
    string? Subject,
    DateTimeOffset? From,
    DateTimeOffset? To,
    long? Before,
    int? Limit)
{
    public IQueryable<AuditEvent> Filter(IQueryable<AuditEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (!string.IsNullOrWhiteSpace(Action))
        {
            string prefix = Action.Trim().ToLowerInvariant();
            events = events.Where(e => e.Action.StartsWith(prefix));
        }

        if (!string.IsNullOrWhiteSpace(Actor))
        {
            string part = Actor.Trim().ToLowerInvariant();
            events = events.Where(e => e.ActorName != null && e.ActorName.ToLower().Contains(part));
        }

        if (!string.IsNullOrWhiteSpace(Subject))
        {
            string id = Subject.Trim();
            events = events.Where(e => e.SubjectId == id);
        }

        if (From is { } after)
        {
            events = events.Where(e => e.OccurredUtc >= after);
        }

        if (To is { } until)
        {
            events = events.Where(e => e.OccurredUtc < until);
        }

        if (Before is { } last)
        {
            events = events.Where(e => e.Id < last);
        }

        return events;
    }
}
