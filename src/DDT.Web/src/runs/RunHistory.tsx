// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Link } from "@tanstack/react-router";

import {
  currentStepLabel,
  type DeploymentSource,
  type DeploymentSummary,
} from "@/deployments/deployments";
import { formatDuration } from "@/lib/format";

import styles from "./RunHistory.module.scss";

export interface RunHistoryProps {
  machineId: string;
  runs: readonly DeploymentSummary[];
  shownId: string | null;
  now: number;
}

const sources: Record<DeploymentSource, string> = {
  Web: "Assigned on the web",
  Rule: "Chosen by a rule, approved on the web",
  Console: "Chosen at the machine",
};

// Every run of the machine, newest first. Choosing one shows it above.
export function RunHistory({ machineId, runs, shownId, now }: RunHistoryProps) {
  return (
    <section className={styles.history} aria-label="History">
      <h3>History</h3>
      {runs.length === 0 ? (
        <p className={styles.empty}>
          This machine has not run a task sequence yet. Assign one above, or add a rule for its
          model on the Rules page.
        </p>
      ) : (
        <table className={styles.table}>
          <thead>
            <tr>
              <th scope="col">Sequence</th>
              <th scope="col">State</th>
              <th scope="col">Source</th>
              <th scope="col">Assigned</th>
              <th scope="col">Duration</th>
            </tr>
          </thead>
          <tbody>
            {runs.map((run) => {
              const step = currentStepLabel(run);
              const end = run.finishedUtc === null ? now : Date.parse(run.finishedUtc);

              return (
                <tr key={run.id} aria-current={run.id === shownId ? "true" : undefined}>
                  <td>
                    <Link
                      to="/machines/$machineId"
                      params={{ machineId }}
                      search={{ run: run.id }}
                      className={styles.link}
                    >
                      {run.title}
                    </Link>
                  </td>
                  <td>
                    {run.state}
                    {run.state === "Failed" && step !== null && (
                      <div className={styles.secondary}>At {step}</div>
                    )}
                  </td>
                  <td>
                    {sources[run.source]}
                    {run.requestedBy !== null && (
                      <div className={styles.secondary}>{run.requestedBy}</div>
                    )}
                  </td>
                  <td>{new Date(run.createdUtc).toLocaleString()}</td>
                  <td>
                    {run.startedUtc === null
                      ? "Not started"
                      : formatDuration(end - Date.parse(run.startedUtc))}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
    </section>
  );
}
