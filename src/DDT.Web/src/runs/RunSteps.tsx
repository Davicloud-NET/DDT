// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconChevronRight } from "@tabler/icons-react";
import { Fragment } from "react";

import {
  deploymentQuery,
  type DeploymentState,
  type DeploymentStepView,
} from "@/deployments/deployments";
import { formattingLocale } from "@/i18n/i18n";
import { formatDuration } from "@/lib/format";
import { useLiveMarks } from "@/live/useLiveMarks";
import type { MachineSummary } from "@/machines/machines";
import { nodeTitle } from "@/sequences/flow/flowKeyboard";
import type { SequenceDefinition } from "@/sequences/sequences";
import { phaseLabel, stepKindLabel } from "@/sequences/steps";
import { Button } from "@/ui/Button";
import { cx } from "@/ui/cx";
import { NodeGlyph } from "@/ui/FlowNode";
import { Panel } from "@/ui/Layout";
import { StateTag } from "@/ui/StateTag";

import type { ResolvedValue } from "@/values/values";

import { decisionLine, runSubjects } from "./decisions";
import { runPath, type PathNode, type PathRun } from "./runPath";
import { skipReason, stepDuration, wentOnAfter } from "./runs";
import { crumbText, pathStateLabel, pathStateTone, stepStateTone } from "./runView";

// Every node on the run's path in order: what it is and where it sits in the tree, how it stands, when it ran, why it
// failed, and what it decided, as the agent recorded it: the branch an IF took, why a node was skipped, when a repeat
// stopped. Nodes of branches the run did not take are left out. A step that ends while the page is open flashes in the
// colour it ended in. The running tint comes and goes at once: fading it would hold the flash back, as a transition
// of a colour wins over an animation of it.
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
}: {
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
}) {
  const { i18n, t } = useLingui();
  const path = runPath(definition, steps, run);
  const subjects = runSubjects(definition, values);
  const shown = path.nodes.filter(
    (each): each is PathNode & { step: DeploymentStepView } =>
      each.state !== "notTaken" && each.step !== null,
  );
  const locale = formattingLocale();
  const clock = (utc: string) => new Date(utc).toLocaleTimeString(locale);
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
        {shown.map((each) => {
          const step = each.step;
          const container = each.entry.number === null;
          const duration = stepDuration(step, now);
          const started = step.startedUtc === null ? null : clock(step.startedUtc);
          const ended = step.finishedUtc === null ? null : clock(step.finishedUtc);
          const took = duration === null ? "" : formatDuration(duration);
          const kind = stepKindLabel(step.kind);
          const phase = phaseLabel(step.phase);
          const decision = decisionLine(each.node, step, subjects);
          const passes = step.pass ?? 0;

          return (
            <li
              key={step.stepId}
              className={cx(
                "flex gap-4 border-b border-line-soft px-4 py-3 last:border-b-0",
                each.state === "running" && !container && "bg-run-soft",
                mark(step.stepId),
              )}
            >
              <span
                className={cx(
                  "flex w-7 shrink-0 pt-0.5 type-numeral",
                  each.state === "running"
                    ? "text-run-text"
                    : each.state === "waiting"
                      ? "text-muted"
                      : "text-ink",
                )}
              >
                {container ? (
                  <NodeGlyph kind={each.node.kind} className="text-ink-2" />
                ) : (
                  String(each.entry.number).padStart(2, "0")
                )}
              </span>
              <div className="flex min-w-0 flex-1 flex-col gap-1">
                {each.ancestors.length > 0 ? <Crumbs node={each} /> : null}
                <span className="flex flex-wrap items-center gap-x-3 gap-y-1">
                  <span className="type-label text-ink">
                    {container ? nodeTitle({ ...each.node, name: step.name }) : step.name}
                  </span>
                  <StateTag tone={pathStateTone[each.state]}>
                    {each.state === "running" && !container
                      ? `${i18n._(pathStateLabel.running)} ${String(step.percent)}%`
                      : i18n._(pathStateLabel[each.state])}
                  </StateTag>
                </span>
                <span className="type-small text-muted">
                  {started === null
                    ? t`${kind}, ${phase}`
                    : ended === null
                      ? each.state === "paused"
                        ? t`${kind}, ${phase}, started ${started}, paused for ${took}`
                        : t`${kind}, ${phase}, started ${started}, running for ${took}`
                      : t`${kind}, ${phase}, ${started} to ${ended}, took ${took}`}
                </span>
                {passes > 1 && !container ? (
                  <span className="type-small text-muted">
                    <Trans>Ran {passes} times, the last is shown.</Trans>
                  </span>
                ) : null}
                {step.state === "Failed" ? (
                  <span className="type-small text-fail-text">
                    {step.error ?? <Trans>The step failed without saying why.</Trans>}
                    {each.node.continueOnError && wentOnAfter(step, steps, runState) ? (
                      <span className="block text-muted">
                        <Trans>
                          The run went on, because "Go on when this step fails" is on for this step.
                        </Trans>
                      </span>
                    ) : null}
                  </span>
                ) : null}
                {decision !== null ? (
                  <span className="type-small text-ink-2">{decision}</span>
                ) : step.state === "Skipped" ? (
                  <span className="type-small text-ink-2">
                    {skipReason(step, each.node, machine)}
                  </span>
                ) : null}
              </div>
              {step.startedUtc !== null ? (
                <Button
                  size="sm"
                  variant="quiet"
                  className="shrink-0 self-start"
                  onPress={() => {
                    onShowLog(step.stepId);
                  }}
                >
                  <Trans>Log</Trans>
                </Button>
              ) : null}
            </li>
          );
        })}
      </ol>
    </Panel>
  );
}

// Where a node sits in the tree: the containers around it, outermost first, and the branch of an IF.
function Crumbs({ node }: { node: PathNode }) {
  return (
    <span className="flex flex-wrap items-center gap-x-1 type-small text-muted">
      <span className="sr-only">{crumbText(node.ancestors)}</span>
      {node.ancestors.map((ancestor, index) => (
        <Fragment key={ancestor.node.id}>
          {index > 0 ? <IconChevronRight aria-hidden="true" size={12} stroke={2} /> : null}
          <span aria-hidden="true">{crumbText([ancestor])}</span>
        </Fragment>
      ))}
    </span>
  );
}
