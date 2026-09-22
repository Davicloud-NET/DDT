// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { DeploymentStepView } from "@/deployments/deployments";
import { formatDuration } from "@/lib/format";
import type { MachineSummary } from "@/machines/machines";
import type { SequenceStep } from "@/sequences/sequences";
import { stepKindLabel } from "@/sequences/steps";
import { skipReason, stepDuration } from "@/runs/runs";

import styles from "./RunStepRow.module.scss";

export interface RunStepRowProps {
  step: DeploymentStepView;
  count: number;
  // The step as the run's frozen sequence defines it.
  planned: SequenceStep | undefined;
  // The run went on after this step.
  wentOn: boolean;
  machine: MachineSummary | null;
  now: number;
  onShowLog: () => void;
}

function clock(utc: string): string {
  return new Date(utc).toLocaleTimeString();
}

export function RunStepRow({
  step,
  count,
  planned,
  wentOn,
  machine,
  now,
  onShowLog,
}: RunStepRowProps) {
  const duration = stepDuration(step, now);

  return (
    <li className={styles.row} data-state={step.state}>
      <div className={styles.heading}>
        <span className={styles.position}>
          Step {String(step.index + 1)} of {String(count)}
        </span>
        <span className={styles.name}>{step.name}</span>
        <span className={styles.secondary}>{stepKindLabel(step.kind)}</span>
        <span className={styles.state} data-state={step.state}>
          {step.state === "Running" ? `Running, ${String(step.percent)}%` : step.state}
        </span>
        {step.startedUtc !== null && (
          <button type="button" className={styles.log} onClick={onShowLog}>
            Show this step's log
          </button>
        )}
      </div>

      {step.startedUtc !== null && (
        <div className={styles.secondary}>
          Started {clock(step.startedUtc)}
          {step.finishedUtc !== null && `, ended ${clock(step.finishedUtc)}`}
          {duration !== null &&
            (step.finishedUtc === null
              ? `, running for ${formatDuration(duration)}`
              : `, took ${formatDuration(duration)}`)}
        </div>
      )}

      {step.state === "Running" && (
        <progress
          className={styles.progress}
          max={100}
          value={step.percent}
          aria-label={`${step.name} progress`}
        />
      )}

      {step.state === "Failed" && (
        <div className={styles.error}>
          {step.error ?? "The step failed without saying why."}
          {planned?.continueOnError === true && wentOn && (
            <div className={styles.secondary}>
              The run continued, because Continue on error is on for this step.
            </div>
          )}
        </div>
      )}

      {step.state === "Skipped" && (
        <div className={styles.secondary}>{skipReason(step, planned, machine)}</div>
      )}
    </li>
  );
}
