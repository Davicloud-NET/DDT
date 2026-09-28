// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import { isActive, isWaiting } from "@/deployments/deployments";
import { relativeTime } from "@/lib/relativeTime";
import type { MachineSummary } from "@/machines/machines";
import { railFromSummary, railLabel } from "@/machines/machineView";
import { SequenceRailStrip } from "@/ui/SequenceRailStrip";

import { detailTone, runDetail } from "./runDetail";
import { RunLines } from "./RunLines";

// What a row says about the machine's run, or why it has none.
export function RunCell({ machine, now }: { machine: MachineSummary; now: number }) {
  const run = machine.deployment;

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

  if (run === null) {
    return <span className="type-small text-muted">{t`No sequence assigned`}</span>;
  }

  const rail = railFromSummary(run);

  return (
    <span className="flex min-w-0 flex-col gap-1.5 pr-6">
      <RunLines
        title={run.title}
        detail={runDetail(machine, now)}
        detailTone={isWaiting(run) ? "attention" : detailTone(run.state)}
      />
      {rail.length > 0 ? <SequenceRailStrip steps={rail} label={railLabel(run)} /> : null}
    </span>
  );
}
