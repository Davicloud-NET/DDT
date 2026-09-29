// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { infiniteQueryOptions, type InfiniteData, type QueryClient } from "@tanstack/react-query";

import { apiGet } from "@/lib/api";

export type AuditActorKind = "User" | "Machine" | "Token" | "System";

// One row of the audit table, like the server's AuditEntry. detail says what changed, in the server's words.
export interface AuditEntry {
  id: number;
  occurredUtc: string;
  action: string;
  actorKind: AuditActorKind;
  actorName: string | null;
  actorUserId: string | null;
  actorMachineId: string | null;
  subjectId: string | null;
  sourceAddress: string | null;
  detail: string | null;
}

export interface AuditPage {
  items: AuditEntry[];
  next: number | null;
}

// action matches the start of an action name, such as "machine." or "deployment.failed". actor matches any part of the
// actor's name. from and to are local dates from the date fields, or "" when not set.
export interface AuditFilter {
  action: string;
  actor: string;
  from: string;
  to: string;
}

export const auditKey = ["audit"] as const;

const PAGE_SIZE = 100;

// The start of a date field's day in the browser's time zone, sent as UTC.
function startOfDay(date: string): string {
  return new Date(`${date}T00:00:00`).toISOString();
}

function dayAfter(date: string): string {
  const day = new Date(`${date}T00:00:00`);
  day.setDate(day.getDate() + 1);

  return day.toISOString();
}

export function auditQuery(filter: AuditFilter) {
  return infiniteQueryOptions({
    queryKey: [...auditKey, filter],
    initialPageParam: null as number | null,
    queryFn: ({ pageParam }) => {
      const search = new URLSearchParams();

      if (filter.action !== "") {
        search.set("action", filter.action);
      }

      if (filter.actor.trim() !== "") {
        search.set("actor", filter.actor.trim());
      }

      if (filter.from !== "") {
        search.set("from", startOfDay(filter.from));
      }

      // The chosen last day is included, so the range ends at the start of the next day.
      if (filter.to !== "") {
        search.set("to", dayAfter(filter.to));
      }

      if (pageParam !== null) {
        search.set("before", String(pageParam));
      }

      search.set("limit", String(PAGE_SIZE));

      return apiGet<AuditPage>(`/api/audit?${search.toString()}`);
    },
    getNextPageParam: (last) => last.next,
  });
}

function passes(filter: AuditFilter, entry: AuditEntry): boolean {
  const occurred = Date.parse(entry.occurredUtc);

  return (
    (filter.action === "" || entry.action.toLowerCase().startsWith(filter.action.toLowerCase())) &&
    (filter.actor.trim() === "" ||
      (entry.actorName ?? "").toLowerCase().includes(filter.actor.trim().toLowerCase())) &&
    (filter.from === "" || occurred >= Date.parse(startOfDay(filter.from))) &&
    (filter.to === "" || occurred < Date.parse(dayAfter(filter.to)))
  );
}

// Handles the hub's auditAppended. It carries the rows one save added, oldest first. They go to the top of every cached
// log whose filter they pass.
export function appendAudit(queryClient: QueryClient, entries: readonly AuditEntry[]): void {
  for (const [key, data] of queryClient.getQueriesData<InfiniteData<AuditPage>>({
    queryKey: auditKey,
  })) {
    const filter = key[1] as AuditFilter | undefined;

    if (data === undefined || filter === undefined || data.pages.length === 0) {
      continue;
    }

    const known = new Set(data.pages.flatMap((page) => page.items.map((entry) => entry.id)));
    const fresh = [...entries]
      .filter((entry) => !known.has(entry.id) && passes(filter, entry))
      .reverse();

    if (fresh.length === 0) {
      continue;
    }

    const [first, ...rest] = data.pages;

    queryClient.setQueryData(key, {
      ...data,
      pages: [
        { ...first, items: [...fresh, ...(first?.items ?? [])], next: first?.next ?? null },
        ...rest,
      ],
    });
  }
}
