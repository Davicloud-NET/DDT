// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import {
  assignedBy,
  isWaiting,
  type DeploymentSummary,
  type DeploymentView,
} from "@/deployments/deployments";
import { formattingLocale } from "@/i18n/i18n";
import { Notice } from "@/ui/Notice";
import { Panel } from "@/ui/Panel";
import { StateTag } from "@/ui/StateTag";

import { LinuxDetours } from "./panel/LinuxDetours";
import { RunActivity } from "./panel/RunActivity";
import { runLine } from "./panel/runLine";
import { RunProgress } from "./panel/RunProgress";
import { RunRail } from "./panel/RunRail";
import { runPath } from "./runPath";
import { secureBootAllowance } from "./runs";
import { runTag } from "./runView";

interface RunPanelProps {
  run: DeploymentSummary;
  // Null until the run's steps have loaded, and for a deployment from before task sequences.
  view: DeploymentView | null;
  // The machine's last contact with the server.
  lastSeenUtc: string;
  // Since when a Linux run waits for the installed Linux's first call after its hand-over. Null otherwise.
  handedOverUtc?: string | null;
  // How often the machine has started from the network again since that hand-over.
  bootReturns?: number;
  // How often in a row the machine started from the network while the run goes on in the installed Linux.
  detours?: number;
  // What to add to now for the server's clock, on which the wait after a hand-over is measured.
  clockOffsetMs?: number;
  now: number;
}

// Where a run is and for how long: its state, the step it is on with that step's percentage in large type, and
// the rail of the steps on its path.
export function RunPanel({
  run,
  view,
  lastSeenUtc,
  handedOverUtc = null,
  bootReturns = 0,
  detours = 0,
  clockOffsetMs = 0,
  now,
}: RunPanelProps) {
  const { i18n } = useLingui();
  const locale = formattingLocale();
  const path =
    view === null || view.steps.length === 0
      ? null
      : runPath(view.definition, view.steps, {
          activity: run.activity,
          pause: view.pause ?? null,
        });
  const allowance = view === null ? null : secureBootAllowance(view);
  const revision = view?.sequenceRevision ?? null;
  const assigned = assignedBy(run);
  const tag = runTag(run);
  const pausedSince = view?.pause?.sinceUtc ?? path?.current?.step?.startedUtc ?? null;

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
        <RunProgress run={run} path={path} />
      </div>

      {allowance !== null ? <p className="type-small text-attention-text">{allowance}</p> : null}

      {run.state === "Running" && !isWaiting(run) ? (
        <RunActivity
          run={run}
          view={view}
          path={path}
          lastSeenUtc={lastSeenUtc}
          handedOverUtc={handedOverUtc}
          bootReturns={bootReturns}
          clockOffsetMs={clockOffsetMs}
          now={now}
        />
      ) : null}

      {run.state === "Running" && detours > 0 ? <LinuxDetours count={detours} /> : null}

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

      <RunRail run={run} view={view} path={path} now={now} />
    </Panel>
  );
}
