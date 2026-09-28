// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { StepState } from "@/sequences/sequences";

import type { DeploymentStepView, DeploymentSummary, DeploymentView } from "./deployments";

const stepOrder: Record<StepState, number> = {
  Pending: 0,
  Running: 1,
  Done: 2,
  Skipped: 2,
  Failed: 2,
};

// Whether a pushed step is at least as new as the page's copy. A higher pass is a new visit of a node in a repeat.
// Within a visit, a step only moves forward, and a repeat only moves on to later iterations.
export function isNewerStep(known: DeploymentStepView, step: DeploymentStepView): boolean {
  const knownPass = known.pass ?? 0;
  const pass = step.pass ?? 0;

  if (pass !== knownPass) {
    return pass > knownPass;
  }

  const order = stepOrder[step.state] - stepOrder[known.state];

  return order !== 0 ? order > 0 : (step.iteration ?? 0) >= (known.iteration ?? 0);
}

// The run with the pushed step in place of its older copy, keyed by the node and its pass.
export function withStep(view: DeploymentView, step: DeploymentStepView): DeploymentView {
  const known = view.steps.find((candidate) => candidate.stepId === step.stepId);

  if (known === undefined || !isNewerStep(known, step)) {
    return view;
  }

  return {
    ...view,
    steps: view.steps.map((candidate) => (candidate.stepId === step.stepId ? step : candidate)),
  };
}

// The later of two copies of one run, which the machine list and a read of the run can each hold.
export function newerRun(a: DeploymentSummary, b: DeploymentSummary): DeploymentSummary {
  return Date.parse(b.updatedUtc) > Date.parse(a.updatedUtc) ? b : a;
}

// The machine list follows the machine's current run live, so the history takes it from there.
export function withCurrentRun(
  history: readonly DeploymentSummary[],
  current: DeploymentSummary | null,
): DeploymentSummary[] {
  if (current === null) {
    return [...history];
  }

  return history.some((run) => run.id === current.id)
    ? history.map((run) => (run.id === current.id ? newerRun(run, current) : run))
    : [current, ...history];
}
