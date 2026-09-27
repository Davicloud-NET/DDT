// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { QueryClient, type InfiniteData } from "@tanstack/react-query";
import { describe, expect, it } from "vitest";

import { deploymentSummary, machineSummary } from "@/test/builders";

import {
  removeMachinesFromRuns,
  renameMachineInRuns,
  runHistoryQuery,
  upsertRun,
  type RunHistoryItem,
  type RunHistoryPage,
} from "./runHistory";

function item(id: string, state: RunHistoryItem["run"]["state"], machineId = "m1"): RunHistoryItem {
  return {
    machineId,
    machineName: "LAB-01",
    machineModel: "Latitude 7450",
    manufacturer: "Dell Inc.",
    primaryMac: "00155D010203",
    deviceKind: "Laptop",
    run: deploymentSummary({ id, state, title: "Install" }),
  };
}

function seed(
  client: QueryClient,
  states: RunHistoryItem["run"]["state"][],
  items: RunHistoryItem[],
) {
  const key = runHistoryQuery({ states, query: "" }).queryKey;
  const counts = { assigned: 0, running: 0, done: 0, failed: 0, cancelled: 0 };

  for (const entry of items) {
    counts[
      entry.run.state === "Cancelled" ? "cancelled" : (entry.run.state.toLowerCase() as "done")
    ] += 1;
  }

  client.setQueryData<InfiniteData<RunHistoryPage>>(key, {
    pages: [{ items, next: null, counts }],
    pageParams: [null],
  });

  return () => client.getQueryData<InfiniteData<RunHistoryPage>>(key)?.pages[0];
}

describe("upsertRun", () => {
  it("puts a new run at the top and counts it", () => {
    const client = new QueryClient();
    const page = seed(client, [], [item("r1", "Done")]);

    upsertRun(client, item("r2", "Running"));

    expect(page()?.items.map((i) => i.run.id)).toEqual(["r2", "r1"]);
    expect(page()?.counts).toMatchObject({ running: 1, done: 1 });
  });

  it("replaces a known run and moves its count to its new state", () => {
    const client = new QueryClient();
    const page = seed(client, [], [item("r1", "Running")]);

    upsertRun(client, item("r1", "Failed"));

    expect(page()?.items.map((i) => i.run.state)).toEqual(["Failed"]);
    expect(page()?.counts).toMatchObject({ running: 0, failed: 1 });
  });

  it("drops a run from a history whose state filter it leaves, and adds it to one it enters", () => {
    const client = new QueryClient();
    const running = seed(client, ["Running"], [item("r1", "Running")]);
    const failed = seed(client, ["Failed"], []);

    upsertRun(client, item("r1", "Failed"));

    expect(running()?.items).toEqual([]);
    expect(failed()?.items.map((i) => i.run.id)).toEqual(["r1"]);
  });
});

describe("the machines of runs", () => {
  it("follows a renamed machine into its older runs, and drops the runs of a removed one", () => {
    const client = new QueryClient();
    const page = seed(client, [], [item("r1", "Done", "m1"), item("r2", "Done", "m2")]);

    renameMachineInRuns(client, machineSummary({ id: "m1", assignedName: "LAB-99", model: "X1" }));

    expect(page()?.items[0]).toMatchObject({ machineName: "LAB-99", machineModel: "X1" });

    removeMachinesFromRuns(client, ["m1"]);

    expect(page()?.items.map((i) => i.run.id)).toEqual(["r2"]);
  });
});
