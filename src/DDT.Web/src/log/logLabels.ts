// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import type { DeploymentStepView } from "@/deployments/deployments";
import { leafNumbers } from "@/runs/runPath";

import type { AgentLogLevel } from "./log";

// A step by its number as the flow and the rail give it; a container, which has none, by its name.
export function stepLabel(
  steps: readonly DeploymentStepView[],
  stepId: string | null,
): string | null {
  const step = steps.find((candidate) => candidate.stepId === stepId);

  if (step === undefined) {
    return null;
  }

  const position = leafNumbers(steps).get(step.stepId) ?? null;
  const name = step.name;

  return position === null ? name : t`step ${position}, ${name}`;
}

export function levelLabel(level: AgentLogLevel): string {
  switch (level) {
    case "Information":
      return t`Information`;
    case "Warning":
      return t`Warnings`;
    case "Error":
      return t`Errors`;
  }
}
