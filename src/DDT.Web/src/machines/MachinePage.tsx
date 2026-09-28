// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { IconChevronLeft } from "@tabler/icons-react";
import { Link, useParams, useSearch } from "@tanstack/react-router";

import { isActive } from "@/deployments/deployments";
import { useNow } from "@/lib/useNow";
import { LogPanel } from "@/log/LogPanel";
import { resolutionText } from "@/rules/rules";
import { useSequenceResolution } from "@/rules/useSequenceResolution";
import { MachineRuns } from "@/runs/MachineRuns";
import { RunPanel } from "@/runs/RunPanel";
import { useRunDetail } from "@/runs/useRunDetail";
import { EmptyState } from "@/ui/EmptyState";
import { Notice } from "@/ui/Notice";
import { Page } from "@/ui/Page";
import { Skeleton } from "@/ui/Skeleton";

import { MachineActionErrors } from "./MachineActionErrors";
import { MachineHeader } from "./page/MachineHeader";
import { RunDetails } from "./page/RunDetails";
import { useStepLog } from "./page/useStepLog";
import { RunWaiting } from "./RunWaiting";
import { useCanDecide } from "./useCanDecide";
import { useMachineActions } from "./useMachineActions";

// One machine's page: the run it shows and what that run waits for, the values, facts, log and every run the machine
// had. It's live. The machine and its run come from the hub's pushes, and the steps, variables and log lines come from
// the machine's watch.
export function MachinePage() {
  const { machineId } = useParams({ from: "/shell/machines/$machineId" });
  const pinnedRunId = useSearch({ from: "/shell/machines/$machineId" }).run ?? null;
  const canDecide = useCanDecide();

  const detail = useRunDetail(machineId, pinnedRunId);
  const resolution = useSequenceResolution(machineId, detail.machine);
  const actions = useMachineActions();
  const now = useNow(1_000);
  const { machine, summary, view, removed } = detail;
  const { logId, filteredStep, showStepLog, showLogFromFlow } = useStepLog(detail.runId);

  return (
    <Page>
      <Link
        to="/machines"
        className="-mb-1 flex w-fit items-center gap-1 type-small text-ink-2 hover:text-ink hover:underline"
      >
        <IconChevronLeft size={16} stroke={2} aria-hidden="true" />
        <Trans>All machines</Trans>
      </Link>

      {detail.machinesError ? (
        <Notice tone="fail">
          <Trans>The machine could not be loaded.</Trans>
        </Notice>
      ) : null}

      {removed ? (
        <EmptyState title={<Trans>This machine was removed</Trans>}>
          <Trans>It registers as a new machine at its next netboot.</Trans>
        </EmptyState>
      ) : machine === null ? (
        <HeaderSkeleton />
      ) : (
        <>
          <MachineHeader
            machine={machine}
            actions={canDecide ? actions : null}
            resolution={resolution.data === undefined ? null : resolutionText(resolution.data)}
            now={now}
          />
          <MachineActionErrors actions={actions} />
        </>
      )}

      {machine !== null && !removed ? (
        <RunWaiting machineId={machineId} run={machine.deployment} view={view} canAct={canDecide} />
      ) : null}

      {detail.newerRunId !== null ? <NewerRunNotice machineId={machineId} /> : null}

      {summary !== null && !removed ? (
        <RunPanel
          run={summary}
          view={view}
          lastSeenUtc={machine?.lastSeenUtc ?? summary.updatedUtc}
          now={now}
        />
      ) : null}

      {detail.detailError ? (
        <Notice tone="fail">
          <Trans>The run could not be loaded.</Trans>
        </Notice>
      ) : null}

      {!removed ? (
        <RunDetails
          machine={machine}
          summary={summary}
          view={view}
          preview={summary === null ? (resolution.data?.values ?? null) : null}
          now={now}
          onShowLog={showStepLog}
          onShowLogFromFlow={showLogFromFlow}
        />
      ) : null}

      {!removed ? (
        <div id={logId} className="flex flex-col">
          <LogPanel
            machineId={machineId}
            runId={detail.runId}
            active={isActive(summary)}
            steps={view?.steps ?? []}
            stepFilter={filteredStep}
            onStepFilterChange={showStepLog}
          />
        </div>
      ) : null}

      {detail.historyError ? (
        <Notice tone="fail">
          <Trans>The runs of this machine could not be loaded.</Trans>
        </Notice>
      ) : null}

      {!removed && (detail.historyLoaded || detail.runs.length > 0) ? (
        <MachineRuns machineId={machineId} runs={detail.runs} shownId={detail.runId} now={now} />
      ) : null}
    </Page>
  );
}

// Links to the run the machine started while the page shows an older, pinned run.
function NewerRunNotice({ machineId }: { machineId: string }) {
  return (
    <Notice tone="info">
      <span className="flex flex-wrap items-center gap-x-3">
        <Trans>A new run started on this machine.</Trans>
        <Link
          to="/machines/$machineId"
          params={{ machineId }}
          search={{}}
          className="font-semibold underline"
        >
          <Trans>Show it</Trans>
        </Link>
      </span>
    </Notice>
  );
}

function HeaderSkeleton() {
  return (
    <div aria-hidden="true" className="flex flex-col gap-4">
      <div className="flex items-center gap-4">
        <Skeleton className="size-14" />
        <div className="flex flex-col gap-2">
          <Skeleton className="h-8 w-64" />
          <Skeleton className="h-4 w-48" />
        </div>
      </div>
      <Skeleton className="h-16 w-full" />
    </div>
  );
}
