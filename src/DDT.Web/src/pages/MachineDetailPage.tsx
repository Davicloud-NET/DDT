// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";
import { Link, useParams, useSearch } from "@tanstack/react-router";
import { useState } from "react";

import { currentUserQuery } from "@/auth/auth";
import { isActive } from "@/deployments/deployments";
import { useNow } from "@/lib/useNow";
import { LogPanel } from "@/log/LogPanel";
import { MachineActionErrors } from "@/machines/MachineActionErrors";
import { MachineHeader } from "@/machines/MachineHeader";
import { useMachineActions } from "@/machines/useMachineActions";
import { useSequenceResolution } from "@/rules/useSequenceResolution";
import { RunHistory } from "@/runs/RunHistory";
import { RunOverview } from "@/runs/RunOverview";
import { RunStepList } from "@/runs/RunStepList";
import { RunTimeline } from "@/runs/RunTimeline";
import { runTimeline } from "@/runs/runs";
import { useRunDetail } from "@/runs/useRunDetail";

import styles from "./MachineDetailPage.module.scss";

// One machine: what it is, the run the page shows with its steps, and every run it had.
export function MachineDetailPage() {
  // Not strict, so the page reads its parameters in any router that has its path, as its tests do.
  const machineId = useParams({ strict: false }).machineId ?? "";
  const pinnedRunId = useSearch({ strict: false }).run ?? null;

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
    <div className={styles.page}>
      <Link to="/" className={styles.back}>
        All machines
      </Link>

      {detail.machinesError && <p className={styles.error}>The machine could not be loaded.</p>}

      {detail.removed && (
        <section className={styles.notice}>
          <h1>Machine removed</h1>
          <p>This machine was removed. It registers as a new machine at its next netboot.</p>
        </section>
      )}

      {machine !== null && (
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

      {detail.newerRunId !== null && (
        <p className={styles.notice} role="status">
          A new run started on this machine.{" "}
          <Link
            to="/machines/$machineId"
            params={{ machineId }}
            search={{}}
            className={styles.link}
          >
            Show it
          </Link>
        </p>
      )}

      {summary !== null && (
        <RunOverview
          run={summary}
          revision={view?.sequenceRevision ?? null}
          lastSeenUtc={machine?.lastSeenUtc ?? summary.updatedUtc}
          now={now}
        />
      )}

      {detail.detailError && <p className={styles.error}>The run could not be loaded.</p>}

      {summary !== null && view !== null && (
        <>
          <RunTimeline entries={runTimeline(machine, summary, view.steps, view.definition)} />
          {view.steps.length === 0 ? (
            <p className={styles.secondary}>
              This deployment ran before task sequences, so it has no steps to show.
            </p>
          ) : (
            <RunStepList
              steps={view.steps}
              runState={summary.state}
              definition={view.definition}
              machine={machine}
              now={now}
              onShowLog={showStepLog}
            />
          )}
        </>
      )}

      {!detail.removed && (
        <LogPanel
          machineId={machineId}
          runId={detail.runId}
          active={isActive(summary)}
          steps={view?.steps ?? []}
          stepFilter={filteredStep}
          onStepFilterChange={showStepLog}
        />
      )}

      {detail.historyError && <p className={styles.error}>The run history could not be loaded.</p>}

      {!detail.removed && (detail.historyLoaded || detail.runs.length > 0) && (
        <RunHistory machineId={machineId} runs={detail.runs} shownId={detail.runId} now={now} />
      )}
    </div>
  );
}
