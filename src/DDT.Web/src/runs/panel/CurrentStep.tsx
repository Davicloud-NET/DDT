// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import type { DeploymentView } from "@/deployments/deployments";
import { formatBytes, formatDuration } from "@/lib/format";
import { phaseLabel, stepKindLabel } from "@/sequences/steps";

import type { PathNode } from "../runPath";

interface CurrentStepProps {
  step: PathNode;
  // How many steps are on the run's path.
  count: number;
  isTree: boolean;
  artifact: DeploymentView["artifacts"][number] | null;
  now: number;
}

// The step that runs now, with its percentage in large type and the file it works on.
export function CurrentStep({ step, count, isTree, artifact, now }: CurrentStepProps) {
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
