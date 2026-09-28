// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import {
  activityLabel,
  currentStepLabel,
  isSilentActivity,
  type DeploymentSummary,
  type DeploymentView,
} from "@/deployments/deployments";
import { formatDuration } from "@/lib/format";

import type { RunPath } from "../runPath";
import { CurrentStep } from "./CurrentStep";

interface RunActivityProps {
  run: DeploymentSummary;
  view: DeploymentView | null;
  path: RunPath | null;
  // The machine's last contact with the server.
  lastSeenUtc: string;
  now: number;
}

// A running run that waits for no one: the step it is on, or what the agent does between steps and how long the
// machine has been silent while it restarts.
export function RunActivity({ run, view, path, lastSeenUtc, now }: RunActivityProps) {
  const current = path?.current ?? null;
  const running =
    current?.state === "running" && current.step !== null && current.entry.number !== null
      ? current
      : null;
  const artifact =
    running === null
      ? null
      : (view?.artifacts.find((candidate) => candidate.stepId === running.node.id) ?? null);
  const activity = activityLabel(run.activity);
  const after = currentStepLabel(run);
  const silentFor = formatDuration(now - Date.parse(lastSeenUtc));

  if (running !== null && (activity === null || run.activity === "Step")) {
    return (
      <CurrentStep
        step={running}
        count={path?.leaves.length ?? 0}
        isTree={path?.isTree === true}
        artifact={artifact}
        now={now}
      />
    );
  }

  return (
    <div className="flex flex-col gap-1 py-1">
      <span className="type-heading text-run-text">
        {activity ?? (after === null ? t`Starting` : t`Running`)}
      </span>
      {activity !== null && after !== null ? (
        <span className="type-small text-ink-2">
          <Trans>After {after}</Trans>
        </span>
      ) : null}
      {isSilentActivity(run.activity) ? (
        <span className="type-small text-ink-2">
          {run.activity === "Restarting" ? (
            <Trans>No contact for {silentFor}, as the machine restarts.</Trans>
          ) : (
            <Trans>
              No contact for {silentFor}. Windows setup runs before the agent starts again, which
              can take a while.
            </Trans>
          )}
        </span>
      ) : null}
    </div>
  );
}
