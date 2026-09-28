// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { DeploymentSummary, DeploymentView } from "@/deployments/deployments";
import { railStepText } from "@/machines/machineView";
import { SequenceRail } from "@/ui/SequenceRail";

import type { RunPath } from "../runPath";
import { runPhases } from "../runView";
import { phasesOf, railSteps } from "./railSteps";

interface RunRailProps {
  run: DeploymentSummary;
  view: DeploymentView | null;
  path: RunPath | null;
  now: number;
}

// The rail of the steps on the run's path, with the phases they run in. For a tree, the steps of branches the run
// didn't take are left out.
export function RunRail({ run, view, path, now }: RunRailProps) {
  const leaves = path?.leaves ?? [];
  const rail = railSteps(run, path, now);
  const phases = runPhases(phasesOf(leaves)).map((phase) => ({
    label:
      phase.phase === "WindowsPE" ? (
        <Trans>In Windows PE</Trans>
      ) : (
        <Trans>In the installed Windows</Trans>
      ),
    steps: phase.steps,
  }));

  return rail.length > 0 ? (
    <SequenceRail
      steps={rail}
      phases={phases}
      describe={railStepText}
      className="pt-1"
      showNames={leaves.length > 0}
    />
  ) : view !== null && view.steps.length === 0 ? (
    <p className="type-small text-muted">
      <Trans>This deployment ran before task sequences, so it has no steps to show.</Trans>
    </p>
  ) : null;
}
