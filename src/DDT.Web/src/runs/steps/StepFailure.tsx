// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { DeploymentState, DeploymentStepView } from "@/deployments/deployments";

import { wentOnAfter } from "../runs";
import type { ShownNode } from "./shownNodes";

interface StepFailureProps {
  node: ShownNode;
  steps: readonly DeploymentStepView[];
  runState: DeploymentState;
}

// Why a step failed, and whether the run continued because the step is allowed to fail.
export function StepFailure({ node, steps, runState }: StepFailureProps) {
  const step = node.step;

  return (
    <span className="type-small text-fail-text">
      {step.error ?? <Trans>The step failed without saying why.</Trans>}
      {node.node.continueOnError && wentOnAfter(step, steps, runState) ? (
        <span className="block text-muted">
          <Trans>The run went on, because "Go on when this step fails" is on for this step.</Trans>
        </span>
      ) : null}
    </span>
  );
}
