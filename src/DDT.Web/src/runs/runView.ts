// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { MessageDescriptor } from "@lingui/core";
import { msg } from "@lingui/core/macro";

import type {
  DeploymentSource,
  DeploymentState,
  DeploymentStepView,
} from "@/deployments/deployments";
import type { StepState } from "@/sequences/sequences";
import type { StateTone } from "@/ui/StateTag";

// How the run pages show a run and its steps: the words and tones of their states, and the phases of the rail.

export const runStateTone: Record<DeploymentState, StateTone> = {
  Assigned: "idle",
  Running: "run",
  Done: "ok",
  Failed: "fail",
  Cancelled: "retired",
};

export const runStateLabel: Record<DeploymentState, MessageDescriptor> = {
  Assigned: msg`Not started`,
  Running: msg`Running`,
  Done: msg`Done`,
  Failed: msg`Failed`,
  Cancelled: msg`Stopped`,
};

export const stepStateTone: Record<StepState, StateTone> = {
  Pending: "idle",
  Running: "run",
  Done: "ok",
  Failed: "fail",
  Skipped: "retired",
};

export const stepStateLabel: Record<StepState, MessageDescriptor> = {
  Pending: msg`Not started`,
  Running: msg`Running`,
  Done: msg`Done`,
  Failed: msg`Failed`,
  Skipped: msg`Skipped`,
};

export const runSourceLabel: Record<DeploymentSource, MessageDescriptor> = {
  Web: msg`Assigned on the web`,
  Rule: msg`Chosen by a rule, approved on the web`,
  Console: msg`Chosen at the machine`,
};

// Consecutive steps in the same phase, for the labels above the rail. A run that stays in one phase needs none.
export function runPhases(
  steps: readonly DeploymentStepView[],
): { phase: DeploymentStepView["phase"]; steps: number }[] {
  const phases: { phase: DeploymentStepView["phase"]; steps: number }[] = [];

  for (const step of [...steps].sort((a, b) => a.index - b.index)) {
    const last = phases.at(-1);

    if (last?.phase === step.phase) {
      last.steps += 1;
    } else {
      phases.push({ phase: step.phase, steps: 1 });
    }
  }

  return phases.length > 1 ? phases : [];
}
