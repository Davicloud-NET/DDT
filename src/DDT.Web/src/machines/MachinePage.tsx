// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { IconChevronLeft } from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { Link, useParams, useSearch } from "@tanstack/react-router";
import { useState } from "react";

import { currentUserQuery } from "@/auth/auth";
import { isActive } from "@/deployments/deployments";
import { relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import { LogPanel } from "@/log/LogPanel";
import { useSequenceResolution } from "@/rules/useSequenceResolution";
import { MachineRuns } from "@/runs/MachineRuns";
import { RunPanel } from "@/runs/RunPanel";
import { runTimeline } from "@/runs/runs";
import { RunSteps } from "@/runs/RunSteps";
import { RunTimeline } from "@/runs/RunTimeline";
import { useRunDetail } from "@/runs/useRunDetail";
import { DeviceGlyph } from "@/ui/DeviceGlyph";
import { EmptyState, Facts, Page, Skeleton } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";
import { StateTag } from "@/ui/StateTag";

import { MachineActionErrors, MachineActions } from "./MachineActions";
import { formatMac, type MachineSummary } from "./machines";
import { deviceKind, displayName, stateLabel, stateTone } from "./machineView";
import { secureBootFact } from "./secureBoot";
import { useMachineActions } from "./useMachineActions";

// One machine: what it is, the run the page shows with its steps and log, and every run it had. Everything on it
// is live: the machine and its run from the hub's machine pushes, the steps and log lines from its watch.
export function MachinePage() {
  const { machineId } = useParams({ from: "/shell/machines/$machineId" });
  const pinnedRunId = useSearch({ from: "/shell/machines/$machineId" }).run ?? null;
  const user = useQuery(currentUserQuery).data ?? null;
  const roles = user?.roles ?? [];
  const canDecide = roles.includes("Administrator") || roles.includes("Operator");

  const detail = useRunDetail(machineId, pinnedRunId);
  const resolution = useSequenceResolution(machineId, detail.machine);
  const actions = useMachineActions();
  const now = useNow(1_000);
  const { machine, summary, view } = detail;

  // A step belongs to one run, so its filter ends when another run is shown.
  const [stepFilter, setStepFilter] = useState<{ runId: string | null; stepId: string } | null>(
    null,
  );
  const filteredStep = stepFilter?.runId === detail.runId ? stepFilter.stepId : null;
  const showStepLog = (stepId: string | null) => {
    setStepFilter(stepId === null ? null : { runId: detail.runId, stepId });
  };

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

      {detail.removed ? (
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
            resolution={resolution.data?.explanation ?? null}
            now={now}
          />
          <MachineActionErrors actions={actions} />
        </>
      )}

      {detail.newerRunId !== null ? (
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
      ) : null}

      {summary !== null && !detail.removed ? (
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

      {summary !== null && view !== null && view.steps.length > 0 && !detail.removed ? (
        <div className="grid items-start gap-4 xl:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)]">
          <RunSteps
            steps={view.steps}
            runState={summary.state}
            definition={view.definition}
            machine={machine}
            now={now}
            onShowLog={showStepLog}
          />
          <RunTimeline entries={runTimeline(machine, summary, view.steps, view.definition)} />
        </div>
      ) : null}

      {!detail.removed ? (
        <LogPanel
          machineId={machineId}
          runId={detail.runId}
          active={isActive(summary)}
          steps={view?.steps ?? []}
          stepFilter={filteredStep}
          onStepFilterChange={showStepLog}
        />
      ) : null}

      {detail.historyError ? (
        <Notice tone="fail">
          <Trans>The runs of this machine could not be loaded.</Trans>
        </Notice>
      ) : null}

      {!detail.removed && (detail.historyLoaded || detail.runs.length > 0) ? (
        <MachineRuns machineId={machineId} runs={detail.runs} shownId={detail.runId} now={now} />
      ) : null}
    </Page>
  );
}

// The machine's name, state and plate of facts, with what an operator can do to it.
function MachineHeader({
  machine,
  actions,
  resolution,
  now,
}: {
  machine: MachineSummary;
  // Null for someone who may only look.
  actions: ReturnType<typeof useMachineActions> | null;
  // Why the machine would get the sequence it gets, as the server explains it.
  resolution: string | null;
  now: number;
}) {
  const { i18n } = useLingui();
  const maker = [machine.manufacturer, machine.model].filter((part) => part !== null).join(" ");
  const signedInBy = machine.signedInBy;
  const seen = relativeTime(machine.lastSeenUtc, now);
  const from = machine.lastSeenAddress;
  const disks =
    machine.disks ??
    (machine.eligibleDiskCount === 0 ? t`No disk DDT can install on` : t`Not reported`);

  return (
    <header className="flex flex-col gap-4">
      <div className="flex flex-wrap items-start gap-x-4 gap-y-3">
        <DeviceGlyph kind={deviceKind(machine)} size="lg" />
        <div className="flex min-w-0 flex-1 flex-col gap-1">
          <span className="flex flex-wrap items-center gap-3">
            <h1 className="type-title text-ink">{displayName(machine)}</h1>
            <StateTag tone={stateTone[machine.state]}>{i18n._(stateLabel[machine.state])}</StateTag>
          </span>
          <span className="text-ink-2">
            {maker === "" ? <Trans>Model not reported</Trans> : maker}
            {signedInBy !== null ? (
              <>
                {". "}
                <Trans>{signedInBy} signed in at the machine.</Trans>
              </>
            ) : null}
          </span>
        </div>
        {actions !== null ? (
          <MachineActions machine={machine} actions={actions} layout="panel" />
        ) : null}
      </div>

      <Facts
        layout="plate"
        items={[
          {
            label: <Trans>Serial</Trans>,
            value: machine.serialNumber ?? t`Not reported`,
            mono: true,
          },
          {
            label: <Trans>MAC address</Trans>,
            value: machine.macAddresses.map(formatMac).join(", "),
            mono: true,
          },
          {
            label: <Trans>Last seen</Trans>,
            value: from === null ? seen : t`${seen} from ${from}`,
          },
          { label: <Trans>Secure Boot</Trans>, value: secureBootFact(machine) },
          { label: <Trans>Disks</Trans>, value: disks },
          { label: <Trans>Agent</Trans>, value: machine.agentVersion ?? t`Not reported` },
          { label: <Trans>SMBIOS UUID</Trans>, value: machine.smbiosUuid, mono: true },
        ]}
      />

      {resolution !== null && !isActive(machine.deployment) ? (
        <p className="type-small text-ink-2">{resolution}</p>
      ) : null}
    </header>
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
