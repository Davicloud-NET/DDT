// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { QueryClient, type InfiniteData } from "@tanstack/react-query";
import { describe, expect, it } from "vitest";

import {
  appendAudit,
  auditQuery,
  type AuditEntry,
  type AuditFilter,
  type AuditPage,
} from "./audit";

function entry(id: number, action: string, actorName = "admin"): AuditEntry {
  return {
    id,
    occurredUtc: "2026-09-27T10:00:00Z",
    action,
    actorKind: "User",
    actorName,
    actorUserId: null,
    actorMachineId: null,
    subjectId: null,
    sourceAddress: null,
    detail: null,
  };
}

function seed(client: QueryClient, filter: AuditFilter, items: AuditEntry[]) {
  const key = auditQuery(filter).queryKey;

  client.setQueryData<InfiniteData<AuditPage>>(key, {
    pages: [{ items, next: null }],
    pageParams: [null],
  });

  return () => client.getQueryData<InfiniteData<AuditPage>>(key)?.pages[0]?.items.map((e) => e.id);
}

const everything: AuditFilter = { action: "", actor: "", from: "", to: "" };

describe("appendAudit", () => {
  it("puts the rows one save added on top, newest first, once", () => {
    const client = new QueryClient();
    const ids = seed(client, everything, [entry(1, "machine.registered")]);

    appendAudit(client, [entry(2, "machine.approved"), entry(3, "deployment.assigned")]);
    appendAudit(client, [entry(3, "deployment.assigned")]);

    expect(ids()).toEqual([3, 2, 1]);
  });

  it("adds a row only to the logs whose filter it passes", () => {
    const client = new QueryClient();
    const machines = seed(client, { ...everything, action: "machine." }, []);
    const bob = seed(client, { ...everything, actor: "bob" }, []);

    appendAudit(client, [entry(1, "machine.approved", "alice"), entry(2, "rule.created", "Bob")]);

    expect(machines()).toEqual([1]);
    expect(bob()).toEqual([2]);
  });
});
