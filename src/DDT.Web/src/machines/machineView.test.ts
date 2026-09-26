// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import type { DeploymentStepView } from "@/deployments/deployments";
import { deploymentSummary, machineSummary } from "@/test/builders";

import { machinesSearch } from "./machineSearch";
import {
  byAttention,
  deviceKind,
  hardwareLine,
  inFilter,
  matchesSearch,
  railFromSteps,
  railFromSummary,
  railLabel,
} from "./machineView";

describe("byAttention", () => {
  it("puts what waits for someone first, then failures, then runs", () => {
    const machines = [
      machineSummary({ id: "done", state: "Done" }),
      machineSummary({ id: "deploying", state: "Deploying" }),
      machineSummary({ id: "pending", state: "Pending" }),
      machineSummary({ id: "failed", state: "Failed" }),
      machineSummary({ id: "retired", state: "Retired" }),
    ];

    expect(machines.sort(byAttention).map((machine) => machine.id)).toEqual([
      "pending",
      "failed",
      "deploying",
      "done",
      "retired",
    ]);
  });

  it("puts the newest first within a state, whenever each was last seen", () => {
    const older = machineSummary({
      id: "older",
      firstSeenUtc: "2026-09-16T08:00:00Z",
      lastSeenUtc: "2026-09-16T12:00:00Z",
    });
    const newer = machineSummary({
      id: "newer",
      firstSeenUtc: "2026-09-16T09:00:00Z",
      lastSeenUtc: "2026-09-16T09:00:00Z",
    });

    expect([older, newer].sort(byAttention).map((machine) => machine.id)).toEqual([
      "newer",
      "older",
    ]);
  });
});

describe("inFilter", () => {
  it("files a pending machine under waiting and an approved one under ready", () => {
    expect(inFilter(machineSummary({ state: "Pending" }), "waiting")).toBe(true);
    expect(inFilter(machineSummary({ state: "Approved" }), "ready")).toBe(true);
    expect(inFilter(machineSummary({ state: "Approved" }), "waiting")).toBe(false);
  });

  it("files rejected machines with the retired ones", () => {
    expect(inFilter(machineSummary({ state: "Rejected" }), "retired")).toBe(true);
  });

  it("shows every machine under all", () => {
    expect(inFilter(machineSummary({ state: "Failed" }), "all")).toBe(true);
  });
});

describe("matchesSearch", () => {
  const machine = machineSummary({
    assignedName: "LAB-PC-07",
    serialNumber: "5CG1234XYZ",
    primaryMac: "00155D010203",
    macAddresses: ["00155D010203"],
    lastSeenAddress: "10.0.4.17",
  });

  it("finds a machine by name, serial and address, ignoring case", () => {
    expect(matchesSearch(machine, "lab-pc")).toBe(true);
    expect(matchesSearch(machine, "5cg1234")).toBe(true);
    expect(matchesSearch(machine, "10.0.4.17")).toBe(true);
  });

  it("finds a MAC with or without separators", () => {
    expect(matchesSearch(machine, "00:15:5D:01:02:03")).toBe(true);
    expect(matchesSearch(machine, "00-15-5d")).toBe(true);
    expect(matchesSearch(machine, "00155d0102")).toBe(true);
  });

  it("needs every word to match", () => {
    expect(matchesSearch(machine, "lab 10.0.4")).toBe(true);
    expect(matchesSearch(machine, "lab 10.0.5")).toBe(false);
  });

  it("matches everything for an empty search", () => {
    expect(matchesSearch(machine, "   ")).toBe(true);
  });
});

describe("hardwareLine", () => {
  it("shows the maker and serial under a model name", () => {
    expect(
      hardwareLine(machineSummary({ manufacturer: "HP", model: "EliteBook", serialNumber: "5CG" })),
    ).toBe("HP, serial 5CG");
  });

  it("shows the model and serial under a computer name", () => {
    expect(
      hardwareLine(
        machineSummary({ assignedName: "LAB-07", model: "EliteBook", serialNumber: "5CG" }),
      ),
    ).toBe("EliteBook, serial 5CG");
  });

  it("falls back to the MAC when nothing else is known", () => {
    expect(
      hardwareLine(
        machineSummary({
          manufacturer: null,
          model: null,
          serialNumber: null,
          primaryMac: "00155D010203",
        }),
      ),
    ).toBe("00:15:5D:01:02:03");
  });
});

describe("deviceKind", () => {
  it("maps the server's kind to the glyph", () => {
    expect(deviceKind(machineSummary({ deviceKind: "Laptop" }))).toBe("laptop");
    expect(deviceKind(machineSummary({ deviceKind: "Virtual" }))).toBe("virtual");
    expect(deviceKind(machineSummary({ deviceKind: "Unknown" }))).toBe("unknown");
  });
});

describe("railFromSummary", () => {
  it("counts the steps before the current one as done", () => {
    const run = deploymentSummary({ state: "Running", stepCount: 4, stepIndex: 2, percent: 40 });

    expect(railFromSummary(run)).toEqual([
      { state: "done" },
      { state: "done" },
      { state: "running", percent: 40 },
      { state: "waiting" },
    ]);
  });

  it("marks the step a run failed on", () => {
    const run = deploymentSummary({ state: "Failed", stepCount: 3, stepIndex: 1 });

    expect(railFromSummary(run).map((step) => step.state)).toEqual(["done", "failed", "waiting"]);
  });

  it("shows every step done once the run is", () => {
    const run = deploymentSummary({ state: "Done", stepCount: 2, stepIndex: 1 });

    expect(railFromSummary(run).map((step) => step.state)).toEqual(["done", "done"]);
  });

  it("shows a run that has not started as waiting", () => {
    const run = deploymentSummary({ state: "Assigned", stepCount: 2, stepIndex: null });

    expect(railFromSummary(run).map((step) => step.state)).toEqual(["waiting", "waiting"]);
  });
});

function step(
  index: number,
  name: string,
  state: DeploymentStepView["state"],
  percent: number,
): DeploymentStepView {
  return {
    stepId: `step-${String(index)}`,
    index,
    name,
    kind: "ApplyImage",
    phase: "WindowsPE",
    state,
    percent,
    startedUtc: null,
    finishedUtc: null,
    error: null,
  };
}

describe("railFromSteps", () => {
  it("orders the steps and keeps skipped ones", () => {
    const rail = railFromSteps([
      step(1, "Apply image", "Running", 30),
      step(0, "Partition", "Done", 100),
      step(2, "Join domain", "Skipped", 0),
    ]);

    expect(rail).toEqual([
      { state: "done", name: "Partition" },
      { state: "running", percent: 30, name: "Apply image" },
      { state: "skipped", name: "Join domain" },
    ]);
  });
});

describe("railLabel", () => {
  it("says where a run stands", () => {
    expect(
      railLabel(deploymentSummary({ state: "Running", stepCount: 5, stepIndex: 1, percent: 12 })),
    ).toBe("Step 2 of 5 running, 12 percent");
    expect(railLabel(deploymentSummary({ state: "Failed", stepCount: 5, stepIndex: 3 }))).toBe(
      "Failed at step 4 of 5",
    );
  });
});

describe("machinesSearch", () => {
  it("keeps a known state, the search text and the selection", () => {
    expect(machinesSearch({ state: "failed", q: "lab", selected: "abc" })).toEqual({
      state: "failed",
      q: "lab",
      selected: "abc",
    });
  });

  it("drops the all state, unknown states and empty values", () => {
    expect(machinesSearch({ state: "all", q: "" })).toEqual({});
    expect(machinesSearch({ state: "sideways", selected: 3 })).toEqual({});
  });
});
