// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";

import {
  activityLabel,
  assignedBy,
  currentStepLabel,
  isSilentActivity,
  type DeploymentSummary,
  type DeploymentView,
} from "@/deployments/deployments";
import { formattingLocale } from "@/i18n/i18n";
import { formatBytes, formatDuration } from "@/lib/format";
import { railFromSteps, railFromSummary, railStepText } from "@/machines/machineView";
import { phaseLabel, stepKindLabel } from "@/sequences/steps";
import { Panel } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";
import { SequenceRail, type RailStep } from "@/ui/SequenceRail";
import { StateTag } from "@/ui/StateTag";

import { runPercent, secureBootAllowance, stepDuration } from "./runs";
import { runPhases, runStateLabel, runStateTone } from "./runView";

// Where a run is and for how long: its state, the step it is on with that step's percentage in large type, and
// the rail of every step with the phases they run in.
export function RunPanel({
  run,
  view,
  lastSeenUtc,
  now,
}: {
  run: DeploymentSummary;
  // Null until the run's steps have loaded, and for a deployment from before task sequences.
  view: DeploymentView | null;
  // The machine's last contact with the server.
  lastSeenUtc: string;
  now: number;
}) {
  const { i18n } = useLingui();
  const locale = formattingLocale();
  const steps = [...(view?.steps ?? [])].sort((a, b) => a.index - b.index);
  const rail: RailStep[] =
    steps.length > 0
      ? railFromSteps(steps).map((step, index) => ({ ...step, meta: stepMeta(steps[index], now) }))
      : railFromSummary(run);
  const phases = runPhases(steps).map((phase) => ({
    label:
      phase.phase === "WindowsPE" ? (
        <Trans>In Windows PE</Trans>
      ) : (
        <Trans>In the installed Windows</Trans>
      ),
    steps: phase.steps,
  }));
  const running = steps.find((step) => step.state === "Running") ?? null;
  const artifact =
    running === null
      ? null
      : (view?.artifacts.find((candidate) => candidate.stepId === running.stepId) ?? null);
  const activity = activityLabel(run.activity);
  const allowance = view === null ? null : secureBootAllowance(view);
  const overall = steps.length > 0 ? runPercent(steps) : run.state === "Done" ? 100 : null;
  const revision = view?.sequenceRevision ?? null;
  const after = currentStepLabel(run);
  const silentFor = formatDuration(now - Date.parse(lastSeenUtc));
  const assigned = assignedBy(run);

  return (
    <Panel>
      <div className="flex flex-wrap items-start gap-x-4 gap-y-1">
        <div className="flex min-w-0 flex-1 flex-col gap-1">
          <span className="flex flex-wrap items-center gap-3">
            <h2 className="type-subtitle">{run.title}</h2>
            <StateTag tone={runStateTone[run.state]}>{i18n._(runStateLabel[run.state])}</StateTag>
          </span>
          <span className="type-small text-muted">{runLine(run, revision, now, locale)}</span>
        </div>
        {overall !== null && run.state === "Running" ? (
          <span className="flex items-baseline gap-2 pt-1">
            <span className="type-numeral text-ink">{overall}%</span>
            <span className="type-small text-muted">
              <Trans>of the run</Trans>
            </span>
          </span>
        ) : null}
      </div>

      {allowance !== null ? <p className="type-small text-attention-text">{allowance}</p> : null}

      {run.state === "Running" ? (
        running !== null && activity === null ? (
          <CurrentStep step={running} count={steps.length} artifact={artifact} now={now} />
        ) : (
          <div className="flex flex-col gap-1 py-1">
            <span className="type-heading text-run-text">
              {activity ?? (after === null ? t`Starting` : t`Running`)}
            </span>
            {activity !== null && after !== null ? (
              <span className="type-small text-ink-2">
                <Trans>After {after}</Trans>
              </span>
            ) : null}
            {isSilentActivity(run.activity) ? (
              <span className="type-small text-ink-2">
                {run.activity === "Restarting" ? (
                  <Trans>No contact for {silentFor}, as the machine restarts.</Trans>
                ) : (
                  <Trans>
                    No contact for {silentFor}. Windows setup runs before the agent starts again,
                    which can take a while.
                  </Trans>
                )}
              </span>
            ) : null}
          </div>
        )
      ) : null}

      {run.state === "Assigned" ? (
        <p className="text-ink-2">
          <Trans>{assigned}. The machine has not started it yet.</Trans>
        </p>
      ) : null}

      {run.state === "Failed" ? (
        <Notice tone="fail">
          {run.error ?? <Trans>The run failed without saying why.</Trans>}
        </Notice>
      ) : null}

      {rail.length > 0 ? (
        <SequenceRail
          steps={rail}
          phases={phases}
          describe={railStepText}
          className="pt-1"
          showNames={steps.length > 0}
        />
      ) : view !== null && view.steps.length === 0 ? (
        <p className="type-small text-muted">
          <Trans>This deployment ran before task sequences, so it has no steps to show.</Trans>
        </p>
      ) : null}
    </Panel>
  );
}

// The step that runs now, with its percentage in large type and the file it works on.
function CurrentStep({
  step,
  count,
  artifact,
  now,
}: {
  step: DeploymentView["steps"][number];
  count: number;
  artifact: DeploymentView["artifacts"][number] | null;
  now: number;
}) {
  const number = step.index + 1;
  const phase = phaseLabel(step.phase);
  const kind = stepKindLabel(step.kind);
  const file = artifact?.name ?? null;
  const size = artifact === null ? "" : formatBytes(artifact.sizeBytes);
  const runningFor =
    step.startedUtc === null ? null : formatDuration(now - Date.parse(step.startedUtc));

  return (
    <div className="flex flex-wrap items-end gap-x-5 gap-y-1 py-1">
      <span className="type-display text-run-text">{step.percent}%</span>
      <span className="flex min-w-0 flex-col gap-0.5 pb-1">
        <span className="type-small text-muted">
          <Trans>
            Step {number} of {count}, {phase}
          </Trans>
        </span>
        <span className="type-heading">{step.name}</span>
        <span className="type-small text-ink-2">
          {file === null ? kind : t`${kind}: ${file}, ${size}`}
        </span>
      </span>
      <span className="flex-1" />
      {runningFor !== null ? (
        <span className="pb-1 type-small text-muted">
          <Trans>Running for {runningFor}</Trans>
        </span>
      ) : null}
    </div>
  );
}

// The short line under a step's number on the rail: its percentage while it runs, else how long it took.
function stepMeta(
  step: DeploymentView["steps"][number] | undefined,
  now: number,
): string | undefined {
  if (step === undefined) {
    return undefined;
  }

  if (step.state === "Running") {
    return `${String(step.percent)}%`;
  }

  const duration = step.state === "Done" ? stepDuration(step, now) : null;

  return duration === null ? undefined : formatDuration(duration);
}

// "Revision 14, started 09:41, running for 12 min", or how the run ended.
function runLine(
  run: DeploymentSummary,
  revision: number | null,
  now: number,
  locale: string,
): string {
  const parts: string[] = [];

  if (revision !== null) {
    parts.push(t`Revision ${revision}`);
  }

  if (run.startedUtc === null) {
    const assigned = new Date(run.createdUtc).toLocaleString(locale);
    parts.push(t`assigned ${assigned}`);
  } else {
    const started = new Date(run.startedUtc).toLocaleString(locale);
    parts.push(t`started ${started}`);

    if (run.finishedUtc === null) {
      const running = formatDuration(now - Date.parse(run.startedUtc));
      parts.push(t`running for ${running}`);
    } else {
      const took = formatDuration(Date.parse(run.finishedUtc) - Date.parse(run.startedUtc));
      parts.push(t`took ${took}`);
    }
  }

  return parts.join(", ");
}
