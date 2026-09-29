// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import {
  deploymentQuery,
  type DeploymentState,
  type DeploymentStepView,
} from "@/deployments/deployments";
import { useLiveMarks } from "@/live/useLiveMarks";
import type { MachineSummary } from "@/machines/machines";
import type { SequenceDefinition } from "@/sequences/sequences";
import { Panel } from "@/ui/Panel";

import type { ResolvedValue } from "@/values/values";

import { runSubjects } from "./decisions";
import { runPath, type PathRun } from "./runPath";
import { stepStateTone } from "./runView";
import { RunStepItem } from "./steps/RunStepItem";
import { shownNodes } from "./steps/shownNodes";

interface RunStepsProps {
  runId: string;
  steps: readonly DeploymentStepView[];
  runState: DeploymentState;
  definition: SequenceDefinition | null;
  machine: MachineSummary | null;
  // What the agent does and the pause it waits at, for a paused step.
  run?: PathRun;
  // The run's values, whose names its conditions may test.
  values?: readonly ResolvedValue[];
  now: number;
  onShowLog: (stepId: string) => void;
}

// Every node on the run's path in order, with what it decided as the agent recorded it. Nodes in branches the run
// didn't take are left out.
export function RunSteps({
  runId,
  steps,
  runState,
  definition,
  machine,
  run = {},
  values = [],
  now,
  onShowLog,
}: RunStepsProps) {
  const { t } = useLingui();
  const path = runPath(definition, steps, run);
  const subjects = runSubjects(definition, values);
  const mark = useLiveMarks({
    queryKey: deploymentQuery(runId).queryKey,
    items: (view) => view.steps,
    id: (step) => step.stepId,
    signature: (step) => `${step.state} ${String(step.pass ?? 0)}`,
    tone: (step) =>
      step.state === "Running" || step.state === "Pending" ? null : stepStateTone[step.state],
  });

  return (
    <Panel title={<Trans>Steps</Trans>} flush>
      <ol aria-label={t`Steps`} className="flex flex-col">
        {shownNodes(path).map((node) => (
          <RunStepItem
            key={node.step.stepId}
            node={node}
            steps={steps}
            runState={runState}
            machine={machine}
            subjects={subjects}
            now={now}
            markClass={mark(node.step.stepId)}
            onShowLog={onShowLog}
          />
        ))}
      </ol>
    </Panel>
  );
}
