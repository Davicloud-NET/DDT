// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import type { DeploymentStepView, DeploymentSummary } from "@/deployments/deployments";
import { formatDuration } from "@/lib/format";
import { railFromPath, railFromSummary } from "@/machines/machineView";
import type { RailStep } from "@/ui/SequenceRail";

import type { PathNode, RunPath } from "../runPath";
import { stepDuration } from "../runs";

// The rail of the leaves on the run's path, each with its short line. Without a path, it's the rail built from the
// summary.
export function railSteps(run: DeploymentSummary, path: RunPath | null, now: number): RailStep[] {
  const leaves = path?.leaves ?? [];

  return path !== null && leaves.length > 0
    ? railFromPath(path).map((step, index) => ({ ...step, meta: leafMeta(leaves[index], now) }))
    : railFromSummary(run);
}

// The phase of each leaf, for the labels above the rail. A leaf the server hasn't reported yet is assumed to run
// in the same phase as the one before it.
export function phasesOf(
  leaves: readonly PathNode[],
): Pick<DeploymentStepView, "index" | "phase">[] {
  let phase: DeploymentStepView["phase"] = "WindowsPE";

  return leaves.map((leaf, index) => {
    phase = leaf.step?.phase ?? phase;

    return { index, phase };
  });
}

// The short line under a step's number on the rail: its percentage while it runs, how long it has been paused, how
// many times a step in a repeat ran, else how long it took.
function leafMeta(leaf: PathNode | undefined, now: number): string | undefined {
  const step = leaf?.step ?? null;

  if (leaf === undefined || step === null) {
    return undefined;
  }

  switch (leaf.state) {
    case "running":
      return `${String(step.percent)}%`;
    case "paused":
      return step.startedUtc === null
        ? undefined
        : formatDuration(now - Date.parse(step.startedUtc));
    case "skipped":
      return t`skipped`;
    case "done": {
      const passes = step.pass ?? 0;

      if (passes > 1) {
        return t`${passes} passes`;
      }

      const duration = stepDuration(step, now);

      return duration === null ? undefined : formatDuration(duration);
    }
    default:
      return undefined;
  }
}
