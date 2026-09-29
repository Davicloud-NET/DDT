// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import type { DeploymentSummary } from "@/deployments/deployments";
import { formattingLocale } from "@/i18n/i18n";
import { formatDuration } from "@/lib/format";
import { phaseLabel } from "@/sequences/steps";

import type { PathNode } from "../runPath";
import { stepDuration } from "../runs";

export function passesText(passes: number): string {
  return t`Ran ${passes} times, the last is shown.`;
}

// When the node ran, in which phase and how long, or why it has no times.
export function timeText(node: PathNode, run: DeploymentSummary, now: number): string {
  const step = node.step;
  const locale = formattingLocale();
  const clock = (utc: string) =>
    new Date(utc).toLocaleTimeString(locale, { hour: "2-digit", minute: "2-digit" });

  if (node.state === "notTaken") {
    return t`The run did not go this way.`;
  }

  if (step === null || (step.startedUtc === null && step.finishedUtc === null)) {
    return t`Not started yet.`;
  }

  const phase = phaseLabel(step.phase);

  if (step.startedUtc === null) {
    const at = clock(step.finishedUtc ?? run.updatedUtc);

    return t`Skipped at ${at} in ${phase}.`;
  }

  const start = clock(step.startedUtc);
  const duration = stepDuration(step, now);
  const took = formatDuration(duration ?? 0);

  if (node.node.kind === "if" && step.branch !== null && step.branch !== undefined) {
    const into =
      run.startedUtc === null
        ? null
        : formatDuration(Date.parse(step.startedUtc) - Date.parse(run.startedUtc));

    return into === null
      ? t`Decided at ${start} in ${phase}.`
      : t`Decided at ${start} in ${phase}, ${into} into the run.`;
  }

  if (step.finishedUtc === null) {
    return node.state === "paused"
      ? t`Paused since ${start} in ${phase}, for ${took}.`
      : t`Running since ${start} in ${phase}, for ${took}.`;
  }

  const end = clock(step.finishedUtc);

  return t`Ran from ${start} to ${end} in ${phase}, took ${took}.`;
}
