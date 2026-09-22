// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { DeploymentState, DeploymentStepView } from "@/deployments/deployments";
import type { MachineSummary } from "@/machines/machines";
import type { SequenceDefinition, SequencePhase } from "@/sequences/sequences";
import { plannedSteps, wentOnAfter } from "@/runs/runs";

import { RunStepRow } from "./RunStepRow";

import styles from "./RunStepList.module.scss";

export interface RunStepListProps {
  steps: readonly DeploymentStepView[];
  runState: DeploymentState;
  definition: SequenceDefinition | null;
  machine: MachineSummary | null;
  now: number;
  onShowLog: (stepId: string) => void;
}

const phaseTitles: Record<SequencePhase, string> = {
  WindowsPE: "Windows PE",
  Windows: "Windows, after the hand-over",
};

// The steps in order, grouped by the phase they run in, as the server decided it.
export function RunStepList({
  steps,
  runState,
  definition,
  machine,
  now,
  onShowLog,
}: RunStepListProps) {
  const planned = plannedSteps(definition);
  const groups: { phase: SequencePhase; steps: DeploymentStepView[] }[] = [];

  for (const step of steps) {
    const last = groups.at(-1);

    if (last?.phase === step.phase) {
      last.steps.push(step);
    } else {
      groups.push({ phase: step.phase, steps: [step] });
    }
  }

  return (
    <section className={styles.list} aria-label="Steps">
      {groups.map((group) => (
        <div key={group.steps[0]?.stepId ?? group.phase}>
          <h3 className={styles.phase}>{phaseTitles[group.phase]}</h3>
          <ol className={styles.steps}>
            {group.steps.map((step) => (
              <RunStepRow
                key={step.stepId}
                step={step}
                count={steps.length}
                planned={planned.get(step.stepId)}
                wentOn={wentOnAfter(step, steps, runState)}
                machine={machine}
                now={now}
                onShowLog={() => {
                  onShowLog(step.stepId);
                }}
              />
            ))}
          </ol>
        </div>
      ))}
    </section>
  );
}
