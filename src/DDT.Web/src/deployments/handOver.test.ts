// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { deploymentSummary, machineSummary } from "@/test/builders";

import { FIRST_CALL_EXPECTED_MS, isOverdue, linuxHandOver, linuxReturns } from "./handOver";

const since = "2026-09-16T10:00:00Z";

describe("the wait for the installed Linux after a hand-over", () => {
  it("lasts from the time the server names, while the Linux run is running", () => {
    const run = deploymentSummary({ state: "Running", phase: "Linux", activity: "Restarting" });

    expect(linuxHandOver(machineSummary({ handedOverUtc: since, deployment: run }))).toBe(since);
  });

  it("is over once the server names no time, as after the agent's first call from there", () => {
    const run = deploymentSummary({ state: "Running", phase: "Linux", activity: "Restarting" });

    expect(linuxHandOver(machineSummary({ handedOverUtc: null, deployment: run }))).toBeNull();
    expect(linuxHandOver(machineSummary({ deployment: run }))).toBeNull();
  });

  // The hand-over to Windows keeps its own words, and a run that ended waits for nothing.
  it("is none for a run that installs Windows, a run that ended, or no run", () => {
    const windows = deploymentSummary({ state: "Running", phase: "Windows" });
    const failed = deploymentSummary({ state: "Failed", phase: "Linux" });

    expect(linuxHandOver(machineSummary({ handedOverUtc: since, deployment: windows }))).toBeNull();
    expect(linuxHandOver(machineSummary({ handedOverUtc: since, deployment: failed }))).toBeNull();
    expect(linuxHandOver(machineSummary({ handedOverUtc: since, deployment: null }))).toBeNull();
  });

  // The boot environment said so with the run's token, each time it sent the machine back to its disk.
  it("counts the machine's starts from the network for as long as it lasts", () => {
    const run = deploymentSummary({ state: "Running", phase: "Linux", activity: "Restarting" });
    const failed = deploymentSummary({ state: "Failed", phase: "Linux" });

    expect(
      linuxReturns(machineSummary({ handedOverUtc: since, bootReturns: 2, deployment: run })),
    ).toBe(2);
    expect(linuxReturns(machineSummary({ handedOverUtc: since, deployment: run }))).toBe(0);
    expect(
      linuxReturns(machineSummary({ handedOverUtc: since, bootReturns: null, deployment: run })),
    ).toBe(0);
    expect(
      linuxReturns(machineSummary({ handedOverUtc: null, bootReturns: 2, deployment: run })),
    ).toBe(0);
    expect(
      linuxReturns(machineSummary({ handedOverUtc: since, bootReturns: 2, deployment: failed })),
    ).toBe(0);
  });

  it("is longer than a first start usually takes from ten minutes on", () => {
    const start = Date.parse(since);

    expect(FIRST_CALL_EXPECTED_MS).toBe(10 * 60_000);
    expect(isOverdue(since, start + 17_000)).toBe(false);
    expect(isOverdue(since, start + FIRST_CALL_EXPECTED_MS - 1)).toBe(false);
    expect(isOverdue(since, start + FIRST_CALL_EXPECTED_MS)).toBe(true);
  });
});
