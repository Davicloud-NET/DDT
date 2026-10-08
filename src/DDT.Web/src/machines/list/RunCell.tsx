// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import { isActive, isWaiting } from "@/deployments/deployments";
import { relativeTime } from "@/lib/relativeTime";
import { environmentLine, restartLine, restartsFailedLine } from "@/machines/bootEnvironment";
import type { MachineSummary } from "@/machines/machines";
import { railFromSummary, railLabel } from "@/machines/machineView";
import { SequenceRailStrip } from "@/ui/SequenceRailStrip";

import { detailTone, runDetail } from "./runDetail";
import { RunLines } from "./RunLines";

// What a row says about the machine's run, or why it has none.
export function RunCell({ machine, now }: { machine: MachineSummary; now: number }) {
  const run = machine.deployment;
  const environment = environmentLine(machine);

  if (machine.state === "Pending" && !isActive(run)) {
    const by = machine.signedInBy;
    const registered = relativeTime(machine.firstSeenUtc, now);

    return (
      <RunLines
        title={by === null ? t`Nobody has signed in yet` : t`${by} signed in at the machine`}
        detail={by === null ? t`Registered ${registered}` : t`Needs your approval`}
        detailTone={by === null ? "muted" : "ink"}
      />
    );
  }

  // Without a run to show, the row says what the machine started, or where it is on its way to.
  if (run === null) {
    return environment === null ? (
      <span className="type-small text-muted">{t`No sequence assigned`}</span>
    ) : (
      <span className="flex min-w-0 flex-col type-small text-muted">
        <span className="truncate">{t`No sequence assigned`}</span>
        <span className="truncate">{environment}</span>
      </span>
    );
  }

  const rail = railFromSummary(run);
  // Under the run: where the machine restarts into for it, or that the restarts did not get it there.
  const restart = restartLine(machine);
  const failed = restartsFailedLine(machine);

  return (
    <span className="flex min-w-0 flex-col gap-1.5 pr-6">
      <RunLines
        title={run.title}
        detail={runDetail(machine, now)}
        detailTone={isWaiting(run) ? "attention" : detailTone(run.state)}
      />
      {rail.length > 0 ? <SequenceRailStrip steps={rail} label={railLabel(run)} /> : null}
      {restart !== null ? <span className="truncate type-small text-muted">{restart}</span> : null}
      {failed !== null ? (
        <span className="truncate type-small font-semibold text-attention-text">{failed}</span>
      ) : null}
    </span>
  );
}
