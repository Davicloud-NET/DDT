import type { ReactNode } from "react";

import type { DeploymentSummary } from "@/deployments/deployments";
import { formatDuration } from "@/lib/format";
import { relativeTime } from "@/lib/relativeTime";

import styles from "./DeploymentCell.module.scss";

export interface DeploymentCellProps {
  deployment: DeploymentSummary | null;
  now: number;
}

// The machine's active deployment, else its latest finished one.
export function DeploymentCell({ deployment, now }: DeploymentCellProps) {
  if (deployment === null) {
    return <span className={styles.secondary}>None</span>;
  }

  return (
    <div className={styles.cell}>
      <div>{deployment.imageName}</div>
      {renderStatus(deployment, now)}
    </div>
  );
}

function renderStatus(deployment: DeploymentSummary, now: number): ReactNode {
  switch (deployment.state) {
    case "Assigned":
      return (
        <div className={styles.secondary}>
          {deployment.requestedBy === null ? "Assigned" : `Assigned by ${deployment.requestedBy}`}
        </div>
      );

    case "Running": {
      const step = deployment.step ?? "Starting";

      return (
        <>
          <div className={styles.state} data-state="Running">
            {step} {deployment.percent}%
          </div>
          <progress
            className={styles.progress}
            max={100}
            value={deployment.percent}
            aria-label={`${step} progress`}
          />
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
            {deployment.step === null ? "Failed" : `Failed at ${deployment.step}`}
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
