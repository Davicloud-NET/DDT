// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { MessageDescriptor } from "@lingui/core";
import { msg } from "@lingui/core/macro";
import { infiniteQueryOptions, type InfiniteData, type QueryClient } from "@tanstack/react-query";

import type { DeploymentState, DeploymentSummary } from "@/deployments/deployments";
import { apiGet } from "@/lib/api";
import type { DeviceKindName, MachineSummary } from "@/machines/machines";

// One run of the run history with the machine it ran on, as the server's RunHistoryItem has it.
export interface RunHistoryItem {
  machineId: string;
  machineName: string | null;
  machineModel: string | null;
  manufacturer: string | null;
  primaryMac: string;
  deviceKind: DeviceKindName;
  run: DeploymentSummary;
}

export interface RunStateCounts {
  assigned: number;
  running: number;
  done: number;
  failed: number;
  cancelled: number;
}

// next is the cursor for the following page. Only the first page has counts. They use the same filters but cover
// every state.
export interface RunHistoryPage {
  items: RunHistoryItem[];
  next: string | null;
  counts: RunStateCounts | null;
}

export interface RunHistoryFilter {
  states: readonly DeploymentState[];
  query: string;
}

export type RunFilter = "all" | "running" | "failed" | "done" | "stopped" | "waiting";

// The states the page filters by, with the count of each from the first page.
export const runFilters: {
  id: RunFilter;
  label: MessageDescriptor;
  states: DeploymentState[];
  count: keyof RunStateCounts | null;
  tone?: "fail";
}[] = [
  { id: "all", label: msg`All`, states: [], count: null },
  { id: "running", label: msg`Running`, states: ["Running"], count: "running" },
  { id: "failed", label: msg`Failed`, states: ["Failed"], count: "failed", tone: "fail" },
  { id: "done", label: msg`Done`, states: ["Done"], count: "done" },
  { id: "stopped", label: msg`Stopped`, states: ["Cancelled"], count: "cancelled" },
  { id: "waiting", label: msg`Not started`, states: ["Assigned"], count: "assigned" },
];

export interface RunHistorySearch {
  state?: RunFilter;
  q?: string;
}

export function runHistorySearch(search: Record<string, unknown>): RunHistorySearch {
  const state = runFilters.find((filter) => filter.id === search.state && filter.id !== "all")?.id;

  return {
    ...(state === undefined ? {} : { state }),
    ...(typeof search.q === "string" && search.q !== "" ? { q: search.q } : {}),
  };
}

export const runHistoryKey = ["run-history"] as const;

const PAGE_SIZE = 50;

export function runHistoryQuery(filter: RunHistoryFilter) {
  return infiniteQueryOptions({
    queryKey: [...runHistoryKey, filter.states, filter.query],
    initialPageParam: null as string | null,
    queryFn: ({ pageParam }) => {
      const search = new URLSearchParams();

      for (const state of filter.states) {
        search.append("state", state);
      }

      if (filter.query.trim() !== "") {
        search.set("query", filter.query.trim());
      }

      if (pageParam !== null) {
        search.set("before", pageParam);
      }

      search.set("limit", String(PAGE_SIZE));

      return apiGet<RunHistoryPage>(`/api/deployments?${search.toString()}`);
    },
    getNextPageParam: (last) => last.next,
  });
}

function matches(filter: readonly unknown[], item: RunHistoryItem): boolean {
  const [, states, query] = filter as [string, readonly DeploymentState[], string];
  const needle = query.trim().toLowerCase();

  return (
    (states.length === 0 || states.includes(item.run.state)) &&
    (needle === "" ||
      [item.machineName, item.machineModel, item.primaryMac, item.run.title]
        .filter((value): value is string => value !== null)
        .some((value) => value.toLowerCase().includes(needle)))
  );
}

// Handles the hub's runChanged. The run replaces its copy in every history read so far. A new run goes to the top
// of each history whose filter it passes. The first page's counts follow the change of state.
export function upsertRun(queryClient: QueryClient, item: RunHistoryItem): void {
  for (const [key, data] of queryClient.getQueriesData<InfiniteData<RunHistoryPage>>({
    queryKey: runHistoryKey,
  })) {
    if (data === undefined) {
      continue;
    }

    const previous = data.pages.flatMap((page) => page.items).find((i) => i.run.id === item.run.id);
    const fits = matches(key, item);
    const pages = data.pages.map((page, index) => {
      let items = page.items.filter((i) => i.run.id !== item.run.id || fits);

      items = items.map((i) => (i.run.id === item.run.id ? item : i));

      if (index === 0 && previous === undefined && fits) {
        items = [item, ...items];
      }

      return {
        ...page,
        items,
        counts:
          index === 0
            ? recount(page.counts, previous?.run.state ?? null, item.run.state)
            : page.counts,
      };
    });

    queryClient.setQueryData(key, { ...data, pages });
  }
}

const countOf: Record<DeploymentState, keyof RunStateCounts> = {
  Assigned: "assigned",
  Running: "running",
  Done: "done",
  Failed: "failed",
  Cancelled: "cancelled",
};

function recount(
  counts: RunStateCounts | null,
  before: DeploymentState | null,
  after: DeploymentState,
): RunStateCounts | null {
  if (counts === null || before === after) {
    return counts;
  }

  const next = { ...counts };

  if (before !== null) {
    next[countOf[before]] = Math.max(0, next[countOf[before]] - 1);
  }

  next[countOf[after]] += 1;

  return next;
}

// A machine's name and model show on every run it had, so a renamed machine's older runs follow it.
export function renameMachineInRuns(queryClient: QueryClient, machine: MachineSummary): void {
  for (const [key, data] of queryClient.getQueriesData<InfiniteData<RunHistoryPage>>({
    queryKey: runHistoryKey,
  })) {
    if (data?.pages.some((page) => page.items.some((i) => i.machineId === machine.id)) !== true) {
      continue;
    }

    queryClient.setQueryData(key, {
      ...data,
      pages: data.pages.map((page) => ({
        ...page,
        items: page.items.map((i) =>
          i.machineId === machine.id
            ? { ...i, machineName: machine.assignedName, machineModel: machine.model }
            : i,
        ),
      })),
    });
  }
}

// A removed machine takes its runs with it.
export function removeMachinesFromRuns(
  queryClient: QueryClient,
  machineIds: readonly string[],
): void {
  const removed = new Set(machineIds);

  for (const [key, data] of queryClient.getQueriesData<InfiniteData<RunHistoryPage>>({
    queryKey: runHistoryKey,
  })) {
    if (data === undefined) {
      continue;
    }

    queryClient.setQueryData(key, {
      ...data,
      pages: data.pages.map((page) => ({
        ...page,
        items: page.items.filter((i) => !removed.has(i.machineId)),
      })),
    });
  }
}
