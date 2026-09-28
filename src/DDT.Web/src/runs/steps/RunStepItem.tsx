// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import type { Subject } from "@/conditions/conditionSubjects";
import type { DeploymentState, DeploymentStepView } from "@/deployments/deployments";
import { formattingLocale } from "@/i18n/i18n";
import { formatDuration } from "@/lib/format";
import type { MachineSummary } from "@/machines/machines";
import { nodeTitle } from "@/sequences/flow/flowLabels";
import { phaseLabel, stepKindLabel } from "@/sequences/steps";
import { Button } from "@/ui/Button";
import { cx } from "@/ui/cx";
import { NodeGlyph } from "@/ui/NodeGlyph";
import { StateTag } from "@/ui/StateTag";

import { decisionLine } from "../decisions";
import { skipReason, stepDuration } from "../runs";
import { pathStateLabel, pathStateTone } from "../runView";
import { Crumbs } from "./Crumbs";
import type { ShownNode } from "./shownNodes";
import { StepFailure } from "./StepFailure";

interface RunStepItemProps {
  node: ShownNode;
  steps: readonly DeploymentStepView[];
  runState: DeploymentState;
  machine: MachineSummary | null;
  subjects: readonly Subject[];
  now: number;
  // The live mark classes. A step that ends while the page is open flashes in the colour it ended in.
  markClass: string;
  onShowLog: (stepId: string) => void;
}

// One node of the run's path. The running tint appears and disappears instantly. Fading it would hold back the
// flash, because a CSS transition on a colour wins over an animation of it.
export function RunStepItem({
  node,
  steps,
  runState,
  machine,
  subjects,
  now,
  markClass,
  onShowLog,
}: RunStepItemProps) {
  const { i18n, t } = useLingui();
  const step = node.step;
  const container = node.entry.number === null;
  const locale = formattingLocale();
  const clock = (utc: string) => new Date(utc).toLocaleTimeString(locale);
  const duration = stepDuration(step, now);
  const started = step.startedUtc === null ? null : clock(step.startedUtc);
  const ended = step.finishedUtc === null ? null : clock(step.finishedUtc);
  const took = duration === null ? "" : formatDuration(duration);
  const kind = stepKindLabel(step.kind);
  const phase = phaseLabel(step.phase);
  const decision = decisionLine(node.node, step, subjects);
  const passes = step.pass ?? 0;

  return (
    <li
      className={cx(
        "flex gap-4 border-b border-line-soft px-4 py-3 last:border-b-0",
        node.state === "running" && !container && "bg-run-soft",
        markClass,
      )}
    >
      <StepNumber node={node} />
      <div className="flex min-w-0 flex-1 flex-col gap-1">
        {node.ancestors.length > 0 ? <Crumbs node={node} /> : null}
        <span className="flex flex-wrap items-center gap-x-3 gap-y-1">
          <span className="type-label text-ink">
            {container ? nodeTitle({ ...node.node, name: step.name }) : step.name}
          </span>
          <StateTag tone={pathStateTone[node.state]}>
            {node.state === "running" && !container
              ? `${i18n._(pathStateLabel.running)} ${String(step.percent)}%`
              : i18n._(pathStateLabel[node.state])}
          </StateTag>
        </span>
        <span className="type-small text-muted">
          {started === null
            ? t`${kind}, ${phase}`
            : ended === null
              ? node.state === "paused"
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
          <StepFailure node={node} steps={steps} runState={runState} />
        ) : null}
        {decision !== null ? (
          <span className="type-small text-ink-2">{decision}</span>
        ) : step.state === "Skipped" ? (
          <span className="type-small text-ink-2">{skipReason(step, node.node, machine)}</span>
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
}

// A leaf's number, or a container's glyph.
function StepNumber({ node }: { node: ShownNode }) {
  return (
    <span
      className={cx(
        "flex w-7 shrink-0 pt-0.5 type-numeral",
        node.state === "running"
          ? "text-run-text"
          : node.state === "waiting"
            ? "text-muted"
            : "text-ink",
      )}
    >
      {node.entry.number === null ? (
        <NodeGlyph kind={node.node.kind} className="text-ink-2" />
      ) : (
        String(node.entry.number).padStart(2, "0")
      )}
    </span>
  );
}
