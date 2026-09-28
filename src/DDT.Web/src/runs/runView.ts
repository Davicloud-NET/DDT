// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { MessageDescriptor } from "@lingui/core";
import { msg, t } from "@lingui/core/macro";

import {
  isWaiting,
  type DeploymentSource,
  type DeploymentState,
  type DeploymentStepView,
  type DeploymentSummary,
} from "@/deployments/deployments";
import { nodeTitle } from "@/sequences/flow/flowLabels";
import type { StepState } from "@/sequences/sequences";
import type { StateTone } from "@/ui/StateTag";

import type { PathNode, PathState } from "./runPath";

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

// A node of a run's path: its state as its step has it, paused where a Pause step holds the run, and not taken in a
// branch the run did not go along.
export const pathStateTone: Record<PathState, StateTone> = {
  waiting: "idle",
  running: "run",
  paused: "attention",
  done: "ok",
  failed: "fail",
  skipped: "retired",
  notTaken: "retired",
};

export const pathStateLabel: Record<PathState, MessageDescriptor> = {
  waiting: msg`Not started`,
  running: msg`Running`,
  paused: msg`Paused`,
  done: msg`Done`,
  failed: msg`Failed`,
  skipped: msg`Skipped`,
  notTaken: msg`Not taken`,
};

// Where a node sits: its containers, outermost first, each with the branch of an IF, such as "If: Is it a Latitude?,
// Then".
export function crumbText(ancestors: PathNode["ancestors"]): string {
  return ancestors
    .map(({ node, branch }) => {
      const title = nodeTitle(node);

      return branch === "then" ? t`${title}, Then` : branch === "else" ? t`${title}, Else` : title;
    })
    .join(", ");
}

// The tag of a run: its state, unless it waits for someone, for answers to its inputs or at a pause.
export function runTag(run: DeploymentSummary): { tone: StateTone; label: MessageDescriptor } {
  if (isWaiting(run)) {
    return {
      tone: "attention",
      label:
        run.activity === "WaitingForInput"
          ? msg`Needs answers`
          : run.activity === "Paused"
            ? msg`Paused`
            : msg`Needs someone`,
    };
  }

  return { tone: runStateTone[run.state], label: runStateLabel[run.state] };
}

export const runSourceLabel: Record<DeploymentSource, MessageDescriptor> = {
  Web: msg`Assigned on the web`,
  Rule: msg`Chosen by a rule, approved on the web`,
  Console: msg`Chosen at the machine`,
};

// Consecutive steps in the same phase, for the labels above the rail. A run that stays in one phase needs none.
export function runPhases(
  steps: readonly Pick<DeploymentStepView, "index" | "phase">[],
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
