// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import { isWaiting, type DeploymentSummary } from "@/deployments/deployments";
import { formatDuration } from "@/lib/format";

// "Revision 14, started 09:41, running for 12 min", or how the run ended.
export function runLine(
  run: DeploymentSummary,
  revision: number | null,
  now: number,
  locale: string,
  pausedSince: string | null,
): string {
  const parts: string[] = [];

  if (revision !== null) {
    parts.push(t`Revision ${revision}`);
  }

  if (run.startedUtc === null) {
    const assigned = new Date(run.createdUtc).toLocaleString(locale);
    parts.push(t`assigned ${assigned}`);
  } else {
    const started = new Date(run.startedUtc).toLocaleString(locale);
    parts.push(t`started ${started}`);

    if (run.finishedUtc === null) {
      if (isWaiting(run) && run.activity === "Paused" && pausedSince !== null) {
        const paused = formatDuration(now - Date.parse(pausedSince));
        parts.push(t`paused for ${paused}`);
      } else {
        const running = formatDuration(now - Date.parse(run.startedUtc));
        parts.push(t`running for ${running}`);
      }
    } else {
      const took = formatDuration(Date.parse(run.finishedUtc) - Date.parse(run.startedUtc));
      parts.push(t`took ${took}`);
    }
  }

  return parts.join(", ");
}
