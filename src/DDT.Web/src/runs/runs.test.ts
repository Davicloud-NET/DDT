// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import type { DeploymentStepView, DeploymentSummary } from "@/deployments/deployments";
import { newStep } from "@/sequences/steps";

import { runPercent, runTimeline } from "./runs";
import { runPhases } from "./runView";

const run: DeploymentSummary = {
  id: "d1",
  sequenceId: "s",
  title: "Install Windows",
  state: "Failed",
  source: "Rule",
  requestedBy: "operator",
  stepCount: 3,
  stepIndex: 2,
  stepName: "Set wallpaper",
  percent: 0,
  phase: "Windows",
  activity: null,
  createdUtc: "2026-09-16T10:00:00Z",
  startedUtc: "2026-09-16T10:01:00Z",
  finishedUtc: "2026-09-16T10:40:00Z",
  updatedUtc: "2026-09-16T10:40:00Z",
  error: "The script ended with exit code 1.",
};

function step(index: number, more: Partial<DeploymentStepView>): DeploymentStepView {
  return {
    stepId: `s${String(index)}`,
    index,
    name: `Step ${String(index)}`,
    kind: "runScript",
    phase: "WindowsPE",
    state: "Done",
    percent: 100,
    startedUtc: null,
    finishedUtc: null,
    error: null,
    ...more,
  };
}

describe("runTimeline", () => {
  it("shows the approval, the hand-over with the time Windows took, and the failure, in order", () => {
    const entries = runTimeline(
      null,
      run,
      [
        step(0, { startedUtc: "2026-09-16T10:01:00Z", finishedUtc: "2026-09-16T10:10:00Z" }),
        step(1, {
          phase: "Windows",
          startedUtc: "2026-09-16T10:25:30Z",
          finishedUtc: "2026-09-16T10:30:00Z",
        }),
        step(2, {
          phase: "Windows",
          state: "Failed",
          startedUtc: "2026-09-16T10:30:00Z",
          finishedUtc: "2026-09-16T10:40:00Z",
        }),
      ],
      null,
    );

    expect(entries.map((entry) => entry.text)).toEqual([
      "operator approved the machine on the web to run Install Windows, which a rule chose",
      "The agent started the run",
      "Handed over to Windows; after Windows setup the agent continued there 15 min 30 s later",
      "The run failed: The script ended with exit code 1.",
    ]);
  });

  it("shows a restart after a step set to restart, and puts the entries in time order", () => {
    // The first report of a run can arrive after a step has ended already.
    const entries = runTimeline(
      null,
      { ...run, state: "Running", finishedUtc: null, startedUtc: "2026-09-16T10:02:00Z" },
      [
        step(0, { startedUtc: "2026-09-16T10:01:00Z", finishedUtc: "2026-09-16T10:01:30Z" }),
        step(1, { state: "Running", startedUtc: "2026-09-16T10:04:00Z" }),
      ],
      {
        version: 1,
        steps: [{ ...newStep("runScript", "s0"), rebootAfter: true }, newStep("runScript", "s1")],
      },
    );

    expect(entries.map((entry) => entry.text)).toEqual([
      "operator approved the machine on the web to run Install Windows, which a rule chose",
      "Restarted after step 1, Step 0; back after 2 min 30 s",
      "The agent started the run",
    ]);
  });

  it("says the hand-over began while no Windows step has started", () => {
    const entries = runTimeline(
      null,
      { ...run, state: "Running", finishedUtc: null, activity: "WaitingForWindowsSetup" },
      [
        step(0, { startedUtc: "2026-09-16T10:01:00Z", finishedUtc: "2026-09-16T10:10:00Z" }),
        step(1, { phase: "Windows", state: "Pending" }),
      ],
      null,
    );

    expect(entries.at(-1)?.text).toBe("The hand-over to Windows began");
  });
});

describe("runPercent", () => {
  it("counts finished and skipped steps whole and the running one by its percentage", () => {
    expect(
      runPercent([
        step(0, {}),
        step(1, { state: "Skipped", percent: 0 }),
        step(2, { state: "Running", percent: 50 }),
        step(3, { state: "Pending", percent: 0 }),
      ]),
    ).toBe(63);
  });

  it("is nothing for a run without steps", () => {
    expect(runPercent([])).toBe(0);
  });
});

describe("runPhases", () => {
  it("groups consecutive steps by phase, in step order", () => {
    expect(
      runPhases([
        step(2, { phase: "Windows" }),
        step(0, {}),
        step(1, {}),
        step(3, { phase: "Windows" }),
      ]),
    ).toEqual([
      { phase: "WindowsPE", steps: 2 },
      { phase: "Windows", steps: 2 },
    ]);
  });

  it("draws no phases for a run that stays in one", () => {
    expect(runPhases([step(0, {}), step(1, {})])).toEqual([]);
  });
});
