// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import type { DeploymentStepView, DeploymentSummary } from "@/deployments/deployments";

import { runTimeline } from "./runs";

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
