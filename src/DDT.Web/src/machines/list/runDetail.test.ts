// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { deploymentSummary, machineSummary } from "@/test/builders";

import { runDetail } from "./runDetail";

const now = Date.parse("2026-09-16T10:20:00Z");

function minutesAgo(minutes: number): string {
  return new Date(now - minutes * 60_000).toISOString();
}

describe("what a row says of a Linux run after its hand-over", () => {
  const restarting = deploymentSummary({
    state: "Running",
    phase: "Linux",
    activity: "Restarting",
    stepIndex: 1,
    stepName: "Write the cloud-init seed",
  });

  it("says since when the installed Linux is awaited", () => {
    const machine = machineSummary({
      lastSeenUtc: minutesAgo(3),
      handedOverUtc: minutesAgo(2),
      deployment: restarting,
    });

    expect(runDetail(machine, now)).toBe(
      "Handed over 2 minutes ago, no call from the installed Linux yet",
    );
  });

  it("says that there is still no call once a first start should be over", () => {
    const machine = machineSummary({ handedOverUtc: minutesAgo(12), deployment: restarting });

    expect(runDetail(machine, now)).toBe(
      "Handed over 12 minutes ago, still no call from the installed Linux",
    );
  });

  // A step there asked for the restart, and the machine is known to come back.
  it("says that the machine restarts once the installed Linux has called", () => {
    const machine = machineSummary({
      agentEnvironment: "Linux",
      lastSeenUtc: minutesAgo(1),
      handedOverUtc: null,
      deployment: restarting,
    });

    expect(runDetail(machine, now)).toBe("Restarting, last contact 1 minute ago");
  });

  it("leaves a run that installs Windows its own words", () => {
    const windows = deploymentSummary({
      state: "Running",
      phase: "Windows",
      activity: "Restarting",
    });
    const machine = machineSummary({
      lastSeenUtc: minutesAgo(4),
      handedOverUtc: minutesAgo(4),
      deployment: windows,
    });

    expect(runDetail(machine, now)).toBe("Restarting, last contact 4 minutes ago");
  });
});
