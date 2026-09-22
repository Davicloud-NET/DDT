// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

import {
  activityLabel,
  currentStepLabel,
  isSilentActivity,
  type DeploymentSummary,
} from "@/deployments/deployments";
import { formatDuration, upperFirst } from "@/lib/format";
import { relativeTime } from "@/lib/relativeTime";

import styles from "./DeploymentCell.module.scss";

export interface DeploymentCellProps {
  deployment: DeploymentSummary | null;
  // The machine's last contact with the server.
  lastSeenUtc: string;
  now: number;
}

// The machine's active run, else its latest finished one.
export function DeploymentCell({ deployment, lastSeenUtc, now }: DeploymentCellProps) {
  if (deployment === null) {
    return <span className={styles.secondary}>None</span>;
  }

  return (
    <div className={styles.cell}>
      <div>{deployment.title}</div>
      {renderStatus(deployment, lastSeenUtc, now)}
    </div>
  );
}

function renderStatus(deployment: DeploymentSummary, lastSeenUtc: string, now: number): ReactNode {
  const step = currentStepLabel(deployment);

  switch (deployment.state) {
    case "Assigned":
      return <div className={styles.secondary}>{assignedBy(deployment)}</div>;

    case "Running": {
      const activity = activityLabel(deployment.activity);
      const label = step === null ? "Starting" : upperFirst(step);

      return (
        <>
          {activity === null ? (
            <>
              <div className={styles.state} data-state="Running">
                {step === null ? label : `${label}, ${String(deployment.percent)}%`}
              </div>
              {step !== null && (
                <progress
                  className={styles.progress}
                  max={100}
                  value={deployment.percent}
                  aria-label={`${label} progress`}
                />
              )}
            </>
          ) : (
            <>
              <div className={styles.state} data-state="Running">
                {activity}
              </div>
              {step !== null && <div className={styles.secondary}>After {step}</div>}
            </>
          )}
          {isSilentActivity(deployment.activity) && (
            <div className={styles.secondary}>Last contact {relativeTime(lastSeenUtc, now)}</div>
          )}
          {deployment.startedUtc !== null && (
            <div className={styles.secondary}>
              Running for {formatDuration(now - Date.parse(deployment.startedUtc))}
            </div>
          )}
        </>
      );
    }

    case "Failed":
      return (
        <>
          <div className={styles.state} data-state="Failed">
            {step === null ? "Failed" : `Failed at ${step}`}
          </div>
          {deployment.error !== null && <div className={styles.error}>{deployment.error}</div>}
        </>
      );

    case "Done":
    case "Cancelled":
      return (
        <div
          className={styles.state}
          data-state={deployment.state}
          title={
            deployment.finishedUtc === null
              ? undefined
              : new Date(deployment.finishedUtc).toLocaleString()
          }
        >
          {deployment.state}
          {deployment.finishedUtc !== null && `, ${relativeTime(deployment.finishedUtc, now)}`}
        </div>
      );
  }
}

function assignedBy(deployment: DeploymentSummary): string {
  const by = deployment.requestedBy;

  switch (deployment.source) {
    case "Web":
      return by === null ? "Assigned" : `Assigned by ${by}`;
    case "Rule":
      return by === null
        ? "Approved with the sequence a rule chose"
        : `Approved by ${by} with the sequence a rule chose`;
    case "Console":
      return by === null ? "Chosen at the machine" : `Chosen at the machine by ${by}`;
  }
}
