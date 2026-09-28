// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import { activityLabel, assignedBy, isSilentActivity } from "@/deployments/deployments";
import { formatDuration } from "@/lib/format";
import { relativeTime } from "@/lib/relativeTime";
import type { MachineSummary } from "@/machines/machines";

export type RunDetailTone = "muted" | "ink" | "run" | "fail" | "attention";

export function detailTone(
  state: NonNullable<MachineSummary["deployment"]>["state"],
): "muted" | "run" | "fail" {
  return state === "Running" ? "run" : state === "Failed" ? "fail" : "muted";
}

// The line beside a run's title in the list: its step or activity, or how it ended.
export function runDetail(machine: MachineSummary, now: number): string | null {
  const run = machine.deployment;

  if (run === null) {
    return null;
  }

  switch (run.state) {
    case "Assigned":
      return assignedBy(run);

    case "Running": {
      const activity = activityLabel(run.activity);

      if (activity !== null) {
        const contact = relativeTime(machine.lastSeenUtc, now);

        return isSilentActivity(run.activity) ? t`${activity}, last contact ${contact}` : activity;
      }

      const step = run.stepName;
      const percent = run.percent;

      return step === null ? t`Starting` : t`${step} ${percent}%`;
    }

    case "Failed": {
      // If the check before the first step failed, the run has no step to name.
      if (run.stepIndex === null) {
        return t`Failed`;
      }

      const number = run.stepIndex + 1;

      return t`Step ${number} failed`;
    }

    case "Done": {
      if (run.startedUtc === null || run.finishedUtc === null) {
        return t`Done`;
      }

      const took = formatDuration(Date.parse(run.finishedUtc) - Date.parse(run.startedUtc));
      const finished = relativeTime(run.finishedUtc, now);

      return t`Took ${took}, finished ${finished}`;
    }

    case "Cancelled": {
      const when = run.finishedUtc === null ? null : relativeTime(run.finishedUtc, now);

      return when === null ? t`Stopped` : t`Stopped ${when}`;
    }
  }
}
