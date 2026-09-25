// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import {
  activityLabel,
  assignedBy,
  currentStepLabel,
  isSilentActivity,
  type DeploymentSummary,
} from "@/deployments/deployments";
import { formatDuration, upperFirst } from "@/lib/format";

import styles from "./RunOverview.module.scss";

export interface RunOverviewProps {
  run: DeploymentSummary;
  // The sequence's revision the run was given, when it has one.
  revision: number | null;
  // The machine's last contact with the server.
  lastSeenUtc: string;
  now: number;
  // What the run was allowed beyond its sequence, such as writing an image not signed for Secure Boot.
  allowance?: string | null;
}

// Where the run is and for how long, in one place above its steps.
export function RunOverview({
  run,
  revision,
  lastSeenUtc,
  now,
  allowance = null,
}: RunOverviewProps) {
  const step = currentStepLabel(run);
  const activity = activityLabel(run.activity);

  return (
    <section className={styles.overview} aria-label="Run">
      <h2>
        {run.title}
        {revision !== null && (
          <span className={styles.secondary}> revision {String(revision)}</span>
        )}
      </h2>

      {allowance !== null && <p className={styles.secondary}>{allowance}</p>}

      {run.state === "Assigned" && <p>{assignedBy(run)}. The machine has not started it yet.</p>}

      {run.state === "Running" && (
        <>
          <p className={styles.state} data-state="Running">
            {activity ??
              (step === null ? "Starting" : `${upperFirst(step)}, ${String(run.percent)}%`)}
          </p>
          {activity === null && step !== null && (
            <progress
              className={styles.progress}
              max={100}
              value={run.percent}
              aria-label={`${upperFirst(step)} progress`}
            />
          )}
          {activity !== null && step !== null && <p className={styles.secondary}>After {step}</p>}
          {run.activity === "Restarting" && (
            <p>Restarting for {formatDuration(now - Date.parse(run.updatedUtc))}.</p>
          )}
          {isSilentActivity(run.activity) && (
            <p>
              No contact for {formatDuration(now - Date.parse(lastSeenUtc))}.
              {run.activity !== "Restarting" &&
                " Windows setup runs before the agent starts again, which can take a while."}
            </p>
          )}
          {run.startedUtc !== null && (
            <p className={styles.secondary}>
              Running for {formatDuration(now - Date.parse(run.startedUtc))}
            </p>
          )}
        </>
      )}

      {run.finishedUtc !== null && (
        <>
          <p className={styles.state} data-state={run.state}>
            {run.state === "Failed" && step !== null ? `Failed at ${step}` : run.state},{" "}
            {new Date(run.finishedUtc).toLocaleString()}
            {run.startedUtc !== null &&
              `, after ${formatDuration(Date.parse(run.finishedUtc) - Date.parse(run.startedUtc))}`}
          </p>
          {run.error !== null && <p className={styles.error}>{run.error}</p>}
        </>
      )}
    </section>
  );
}
