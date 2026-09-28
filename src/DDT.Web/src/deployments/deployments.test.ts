// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { deploymentSummary, deploymentView, stepView } from "@/test/builders";

import { isWaiting, withStep, type DeploymentStepView } from "./deployments";

function stateAfter(known: Partial<DeploymentStepView>, pushed: Partial<DeploymentStepView>) {
  const view = deploymentView({ steps: [stepView({ stepId: "s1", ...known })] });

  return withStep(view, stepView({ stepId: "s1", ...pushed })).steps[0];
}

describe("withStep", () => {
  it("moves a step forward within a visit and never back", () => {
    expect(
      stateAfter({ state: "Running", pass: 1 }, { state: "Done", pass: 1, percent: 100 })?.state,
    ).toBe("Done");
    expect(stateAfter({ state: "Done", pass: 1 }, { state: "Running", pass: 1 })?.state).toBe(
      "Done",
    );
    expect(
      stateAfter(
        { state: "Running", pass: 1, percent: 40 },
        { state: "Running", pass: 1, percent: 60 },
      )?.percent,
    ).toBe(60);
  });

  it("takes a higher pass as a new visit, even where it starts over", () => {
    const next = stateAfter({ state: "Done", pass: 1 }, { state: "Running", pass: 2, percent: 5 });

    expect(next?.state).toBe("Running");
    expect(next?.pass).toBe(2);
  });

  it("ignores an older visit that arrives late", () => {
    expect(stateAfter({ state: "Running", pass: 3 }, { state: "Done", pass: 2 })?.state).toBe(
      "Running",
    );
  });

  it("moves a repeat on to its next time through its body, never back", () => {
    expect(
      stateAfter(
        { kind: "repeat", state: "Running", pass: 1, iteration: 2 },
        { kind: "repeat", state: "Running", pass: 1, iteration: 3 },
      )?.iteration,
    ).toBe(3);
    expect(
      stateAfter(
        { kind: "repeat", state: "Running", pass: 1, iteration: 3 },
        { kind: "repeat", state: "Running", pass: 1, iteration: 2 },
      )?.iteration,
    ).toBe(3);
  });

  it("takes the branch and the evaluation an IF pushes", () => {
    const next = stateAfter(
      { kind: "if", state: "Running", pass: 1 },
      {
        kind: "if",
        state: "Running",
        pass: 1,
        branch: "Else",
        evaluation: [{ path: "test", held: false, actual: "OptiPlex 7010" }],
      },
    );

    expect(next?.branch).toBe("Else");
    expect(next?.evaluation).toEqual([{ path: "test", held: false, actual: "OptiPlex 7010" }]);
  });

  it("leaves a run alone for a step it does not have", () => {
    const view = deploymentView({ steps: [stepView({ stepId: "s1" })] });

    expect(withStep(view, stepView({ stepId: "other", state: "Done" }))).toBe(view);
  });
});

describe("isWaiting", () => {
  it("says a running run needs someone while it waits for answers or at a pause", () => {
    expect(isWaiting(deploymentSummary({ state: "Running", activity: "Paused" }))).toBe(true);
    expect(isWaiting(deploymentSummary({ state: "Running", activity: "WaitingForInput" }))).toBe(
      true,
    );
    expect(isWaiting(deploymentSummary({ state: "Running", waiting: true }))).toBe(true);
    expect(isWaiting(deploymentSummary({ state: "Running", activity: "Step" }))).toBe(false);
    expect(isWaiting(deploymentSummary({ state: "Failed", activity: "Paused" }))).toBe(false);
    expect(isWaiting(null)).toBe(false);
  });
});
