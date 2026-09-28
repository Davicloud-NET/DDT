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
  isWaiting,
  type DeploymentStepView,
  type DeploymentSummary,
  type DeploymentView,
} from "@/deployments/deployments";
import { formattingLocale } from "@/i18n/i18n";
import { formatBytes, formatDuration } from "@/lib/format";
import { railFromPath, railFromSummary, railStepText } from "@/machines/machineView";
import { phaseLabel, stepKindLabel } from "@/sequences/steps";
import { Panel } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";
import { SequenceRail, type RailStep } from "@/ui/SequenceRail";
import { StateTag } from "@/ui/StateTag";

import { pathPercent, reachedCount, runPath, type PathNode } from "./runPath";
import { secureBootAllowance, stepDuration } from "./runs";
import { runPhases, runTag } from "./runView";

// Where a run is and for how long: its state, the step it is on with that step's percentage in large type, and
// the rail of the steps on its path with the phases they run in. A run of a tree leaves out the steps of the branches
// it did not take, and counts the steps of its path.
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
  const path =
    view === null || view.steps.length === 0
      ? null
      : runPath(view.definition, view.steps, {
          activity: run.activity,
          pause: view.pause ?? null,
        });
  const leaves = path?.leaves ?? [];
  const rail: RailStep[] =
    path !== null && leaves.length > 0
      ? railFromPath(path).map((step, index) => ({ ...step, meta: leafMeta(leaves[index], now) }))
      : railFromSummary(run);
  const phases = runPhases(phasesOf(leaves)).map((phase) => ({
    label:
      phase.phase === "WindowsPE" ? (
        <Trans>In Windows PE</Trans>
      ) : (
        <Trans>In the installed Windows</Trans>
      ),
    steps: phase.steps,
  }));
  const current = path?.current ?? null;
  const running =
    current?.state === "running" && current.step !== null && current.entry.number !== null
      ? current
      : null;
  const artifact =
    running === null
      ? null
      : (view?.artifacts.find((candidate) => candidate.stepId === running.node.id) ?? null);
  const waiting = isWaiting(run);
  const activity = activityLabel(run.activity);
  const allowance = view === null ? null : secureBootAllowance(view);
  const overall = path !== null ? pathPercent(path) : run.state === "Done" ? 100 : null;
  const revision = view?.sequenceRevision ?? null;
  const after = currentStepLabel(run);
  const silentFor = formatDuration(now - Date.parse(lastSeenUtc));
  const assigned = assignedBy(run);
  const tag = runTag(run);
  const reached = path === null ? 0 : reachedCount(path);
  const count = leaves.length;
  const pausedSince = view?.pause?.sinceUtc ?? current?.step?.startedUtc ?? null;

  return (
    <Panel>
      <div className="flex flex-wrap items-start gap-x-4 gap-y-1">
        <div className="flex min-w-0 flex-1 flex-col gap-1">
          <span className="flex flex-wrap items-center gap-3">
            <h2 className="type-subtitle">{run.title}</h2>
            <StateTag tone={tag.tone}>{i18n._(tag.label)}</StateTag>
          </span>
          <span className="type-small text-muted">
            {runLine(run, revision, now, locale, pausedSince)}
          </span>
        </div>
        {path?.isTree === true && count > 0 ? (
          <span className="flex items-baseline gap-2 pt-1 type-small text-muted">
            <Trans>
              <span className="type-numeral text-ink">
                {reached} of {count}
              </span>{" "}
              steps on this path
            </Trans>
          </span>
        ) : overall !== null && run.state === "Running" ? (
          <span className="flex items-baseline gap-2 pt-1">
            <span className="type-numeral text-ink">{overall}%</span>
            <span className="type-small text-muted">
              <Trans>of the run</Trans>
            </span>
          </span>
        ) : null}
      </div>

      {allowance !== null ? <p className="type-small text-attention-text">{allowance}</p> : null}

      {run.state === "Running" && !waiting ? (
        running !== null && (activity === null || run.activity === "Step") ? (
          <CurrentStep
            step={running}
            count={count}
            isTree={path?.isTree === true}
            artifact={artifact}
            now={now}
          />
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
          showNames={leaves.length > 0}
        />
      ) : view !== null && view.steps.length === 0 ? (
        <p className="type-small text-muted">
          <Trans>This deployment ran before task sequences, so it has no steps to show.</Trans>
        </p>
      ) : null}
    </Panel>
  );
}

// The phase of each leaf, for the labels above the rail; a leaf the server has not reported yet runs where the one
// before it does.
function phasesOf(leaves: readonly PathNode[]): Pick<DeploymentStepView, "index" | "phase">[] {
  let phase: DeploymentStepView["phase"] = "WindowsPE";

  return leaves.map((leaf, index) => {
    phase = leaf.step?.phase ?? phase;

    return { index, phase };
  });
}

// The step that runs now, with its percentage in large type and the file it works on.
function CurrentStep({
  step,
  count,
  isTree,
  artifact,
  now,
}: {
  step: PathNode;
  // The steps on the run's path.
  count: number;
  isTree: boolean;
  artifact: DeploymentView["artifacts"][number] | null;
  now: number;
}) {
  const reported = step.step;
  const number = step.entry.number ?? (reported?.index ?? 0) + 1;
  const phase = phaseLabel(reported?.phase ?? "WindowsPE");
  const kind = stepKindLabel(step.node.kind);
  const file = artifact?.name ?? null;
  const size = artifact === null ? "" : formatBytes(artifact.sizeBytes);
  const started = reported?.startedUtc ?? null;
  const runningFor = started === null ? null : formatDuration(now - Date.parse(started));
  const name = reported?.name ?? step.node.name;

  return (
    <div className="flex flex-wrap items-end gap-x-5 gap-y-1 py-1">
      <span className="type-display text-run-text">{reported?.percent ?? 0}%</span>
      <span className="flex min-w-0 flex-col gap-0.5 pb-1">
        <span className="type-small text-muted">
          {isTree ? (
            <Trans>
              Step {number}, {phase}
            </Trans>
          ) : (
            <Trans>
              Step {number} of {count}, {phase}
            </Trans>
          )}
        </span>
        <span className="type-heading">{name}</span>
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

// The short line under a step's number on the rail: its percentage while it runs, how long it has been paused, how
// many times a step in a repeat ran, else how long it took.
function leafMeta(leaf: PathNode | undefined, now: number): string | undefined {
  const step = leaf?.step ?? null;

  if (leaf === undefined || step === null) {
    return undefined;
  }

  switch (leaf.state) {
    case "running":
      return `${String(step.percent)}%`;
    case "paused":
      return step.startedUtc === null
        ? undefined
        : formatDuration(now - Date.parse(step.startedUtc));
    case "skipped":
      return t`skipped`;
    case "done": {
      const passes = step.pass ?? 0;

      if (passes > 1) {
        return t`${passes} passes`;
      }

      const duration = stepDuration(step, now);

      return duration === null ? undefined : formatDuration(duration);
    }
    default:
      return undefined;
  }
}

// "Revision 14, started 09:41, running for 12 min", or how the run ended.
function runLine(
  run: DeploymentSummary,
  revision: number | null,
  now: number,
  locale: string,
  pausedSince: string | null,
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
      if (isWaiting(run) && run.activity === "Paused" && pausedSince !== null) {
        const paused = formatDuration(now - Date.parse(pausedSince));
        parts.push(t`paused for ${paused}`);
      } else {
        const running = formatDuration(now - Date.parse(run.startedUtc));
        parts.push(t`running for ${running}`);
      }
    } else {
      const took = formatDuration(Date.parse(run.finishedUtc) - Date.parse(run.startedUtc));
      parts.push(t`took ${took}`);
    }
  }

  return parts.join(", ");
}
