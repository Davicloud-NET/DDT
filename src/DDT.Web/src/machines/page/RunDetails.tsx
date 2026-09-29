// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { lazy, Suspense } from "react";

import type { DeploymentSummary, DeploymentView } from "@/deployments/deployments";
import { MachineFacts } from "@/machines/MachineFacts";
import type { MachineSummary } from "@/machines/machines";
import { MachineValues } from "@/machines/MachineValues";
import { runTimeline } from "@/runs/runs";
import { RunSteps } from "@/runs/RunSteps";
import { RunTimeline } from "@/runs/RunTimeline";
import { Skeleton } from "@/ui/Skeleton";
import { valueRows } from "@/values/valueRows";
import type { ResolvedValue } from "@/values/values";

// The run's flow is loaded lazily, because only a run's page needs the flow code.
const RunFlow = lazy(() => import("@/runs/RunFlow"));

interface RunDetailsProps {
  machine: MachineSummary | null;
  summary: DeploymentSummary | null;
  view: DeploymentView | null;
  // The values a run would start with, for a machine without a run.
  preview: ResolvedValue[] | null;
  now: number;
  onShowLog: (stepId: string | null) => void;
  onShowLogFromFlow: (stepId: string) => void;
}

// The run the page shows: its flow, values, steps and timeline, and what the machine reported.
export function RunDetails({
  machine,
  summary,
  view,
  preview,
  now,
  onShowLog,
  onShowLogFromFlow,
}: RunDetailsProps) {
  const shownView =
    view !== null && summary !== null && view.summary.id === summary.id ? view : null;
  const flow = shownView !== null && (shownView.definition?.steps.length ?? 0) > 0;
  const runValues = shownView?.values ?? null;
  const values =
    runValues !== null ? (
      <MachineValues
        title={<Trans>Values of this run</Trans>}
        rows={valueRows({
          values: runValues,
          variables: shownView?.variables ?? null,
          definition: shownView?.definition ?? null,
          steps: shownView?.steps ?? [],
          inputs: shownView?.inputs ?? null,
        })}
        empty={<Trans>No rule, machine role or input gave this run a value.</Trans>}
      />
    ) : preview !== null ? (
      <MachineValues
        title={<Trans>Values a run would start with</Trans>}
        rows={valueRows({ values: preview })}
        empty={<Trans>No rule, machine role or sequence sets a value for this machine.</Trans>}
      />
    ) : null;

  return (
    <>
      {flow && summary !== null ? (
        <Suspense fallback={<Skeleton className="h-[32rem] w-full rounded-panel" />}>
          <RunFlow
            key={shownView.summary.id}
            view={shownView}
            summary={summary}
            now={now}
            onShowLog={onShowLogFromFlow}
          >
            {values}
          </RunFlow>
        </Suspense>
      ) : null}

      {summary !== null && view !== null && view.steps.length > 0 ? (
        <div className="grid items-start gap-4 xl:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)]">
          <RunSteps
            runId={view.summary.id}
            steps={view.steps}
            runState={summary.state}
            definition={view.definition}
            machine={machine}
            run={{ activity: summary.activity, pause: view.pause ?? null }}
            values={view.values ?? []}
            now={now}
            onShowLog={onShowLog}
          />
          <div className="flex min-w-0 flex-col gap-4">
            <RunTimeline entries={runTimeline(machine, summary, view.steps, view.definition)} />
            {machine !== null ? <MachineFacts machine={machine} /> : null}
          </div>
        </div>
      ) : machine !== null ? (
        <div className="grid items-start gap-4 xl:grid-cols-2">
          {flow ? null : values}
          <MachineFacts machine={machine} />
        </div>
      ) : null}
    </>
  );
}
