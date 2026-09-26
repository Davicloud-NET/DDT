// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import type { DeploymentState, DeploymentStepView } from "@/deployments/deployments";
import { formattingLocale } from "@/i18n/i18n";
import { formatDuration } from "@/lib/format";
import type { MachineSummary } from "@/machines/machines";
import type { SequenceDefinition } from "@/sequences/sequences";
import { phaseLabel, stepKindLabel } from "@/sequences/steps";
import { Button } from "@/ui/Button";
import { cx } from "@/ui/cx";
import { Panel } from "@/ui/Layout";
import { StateTag } from "@/ui/StateTag";

import { plannedSteps, skipReason, stepDuration, wentOnAfter } from "./runs";
import { stepStateLabel, stepStateTone } from "./runView";

// Every step of a run in order: what it is, how it stands, when it ran, and why it failed or was skipped.
export function RunSteps({
  steps,
  runState,
  definition,
  machine,
  now,
  onShowLog,
}: {
  steps: readonly DeploymentStepView[];
  runState: DeploymentState;
  definition: SequenceDefinition | null;
  machine: MachineSummary | null;
  now: number;
  onShowLog: (stepId: string) => void;
}) {
  const { i18n, t } = useLingui();
  const planned = plannedSteps(definition);
  const ordered = [...steps].sort((a, b) => a.index - b.index);
  const locale = formattingLocale();
  const clock = (utc: string) => new Date(utc).toLocaleTimeString(locale);

  return (
    <Panel title={<Trans>Steps</Trans>} flush>
      <ol aria-label={t`Steps`} className="flex flex-col">
        {ordered.map((step) => {
          const duration = stepDuration(step, now);
          const plan = planned.get(step.stepId);
          const number = String(step.index + 1).padStart(2, "0");
          const started = step.startedUtc === null ? null : clock(step.startedUtc);
          const ended = step.finishedUtc === null ? null : clock(step.finishedUtc);
          const took = duration === null ? "" : formatDuration(duration);
          const kind = stepKindLabel(step.kind);
          const phase = phaseLabel(step.phase);

          return (
            <li
              key={step.stepId}
              className={cx(
                "flex gap-4 border-b border-line-soft px-4 py-3 last:border-b-0",
                step.state === "Running" && "bg-run-soft",
              )}
            >
              <span
                className={cx(
                  "w-7 shrink-0 pt-0.5 type-numeral",
                  step.state === "Running"
                    ? "text-run-text"
                    : step.state === "Pending"
                      ? "text-muted"
                      : "text-ink",
                )}
              >
                {number}
              </span>
              <div className="flex min-w-0 flex-1 flex-col gap-1">
                <span className="flex flex-wrap items-center gap-x-3 gap-y-1">
                  <span className="type-label text-ink">{step.name}</span>
                  <StateTag tone={stepStateTone[step.state]}>
                    {step.state === "Running"
                      ? `${i18n._(stepStateLabel.Running)} ${String(step.percent)}%`
                      : i18n._(stepStateLabel[step.state])}
                  </StateTag>
                </span>
                <span className="type-small text-muted">
                  {started === null
                    ? t`${kind}, ${phase}`
                    : ended === null
                      ? t`${kind}, ${phase}, started ${started}, running for ${took}`
                      : t`${kind}, ${phase}, ${started} to ${ended}, took ${took}`}
                </span>
                {step.state === "Failed" ? (
                  <span className="type-small text-fail-text">
                    {step.error ?? <Trans>The step failed without saying why.</Trans>}
                    {plan?.continueOnError === true && wentOnAfter(step, steps, runState) ? (
                      <span className="block text-muted">
                        <Trans>
                          The run went on, because "Go on when this step fails" is on for this step.
                        </Trans>
                      </span>
                    ) : null}
                  </span>
                ) : null}
                {step.state === "Skipped" ? (
                  <span className="type-small text-ink-2">{skipReason(step, plan, machine)}</span>
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
