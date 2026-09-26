// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef } from "react";

import {
  deploymentQuery,
  isActive,
  machineDeploymentsQuery,
  newerRun,
  withCurrentRun,
  withStep,
} from "@/deployments/deployments";
import { liveListOptions, POLL_MS } from "@/live/freshness";
import { useMachineWatch } from "@/live/useMachineWatch";
import { machinesQuery } from "@/machines/machines";

// One machine and the run its page shows: the pinned run, else the machine's current or latest run. Changes
// arrive live: the machine list follows the machine and its current run, and step changes patch the run.
export function useRunDetail(machineId: string, pinnedRunId: string | null) {
  const queryClient = useQueryClient();

  const status = useMachineWatch(machineId, {
    onRunStepChanged: (event) => {
      queryClient.setQueryData(deploymentQuery(event.deploymentId).queryKey, (view) =>
        view === undefined ? view : withStep(view, event.step),
      );
    },
    // Changes made while the connection was down were not pushed.
    onReconnect: () => {
      void queryClient.invalidateQueries({
        queryKey: machineDeploymentsQuery(machineId).queryKey,
      });
      // Every run read so far, of which only the shown one is read again.
      void queryClient.invalidateQueries({ queryKey: ["deployment"] });
    },
  });

  const polling = status !== "live";

  const machines = useQuery({ ...machinesQuery, ...liveListOptions(status) });
  const machine = machines.data?.find((candidate) => candidate.id === machineId) ?? null;
  const current = machine?.deployment ?? null;

  // The history takes only the current run from the machine list, so a run that stops being current keeps
  // its last copy from there, and the history is read again.
  const lastCurrent = useRef(current);
  useEffect(() => {
    const previous = lastCurrent.current;
    lastCurrent.current = current;

    if (previous !== null && previous.id !== current?.id) {
      const key = machineDeploymentsQuery(machineId).queryKey;
      queryClient.setQueryData(key, (runs) =>
        runs === undefined ? runs : withCurrentRun(runs, previous),
      );
      void queryClient.invalidateQueries({ queryKey: key });
    }
  }, [current, machineId, queryClient]);

  const history = useQuery(machineDeploymentsQuery(machineId));
  const runs = withCurrentRun(history.data ?? [], current);
  const runId = pinnedRunId ?? current?.id ?? runs[0]?.id ?? null;
  const listed = runs.find((run) => run.id === runId) ?? null;

  const detail = useQuery({
    ...deploymentQuery(runId ?? ""),
    enabled: runId !== null,
    refetchInterval: polling && isActive(listed) ? POLL_MS : false,
  });
  const view = detail.data?.summary.id === runId ? detail.data : undefined;
  const summary =
    listed === null
      ? (view?.summary ?? null)
      : view === undefined
        ? listed
        : newerRun(listed, view.summary);

  return {
    status,
    machine,
    // The list loaded and the machine is not in it.
    removed: machines.isSuccess && machine === null,
    machinesError: machines.isError,
    runs,
    historyLoaded: history.isSuccess,
    historyError: history.isError,
    runId,
    summary,
    view: view ?? null,
    detailError: detail.isError,
    // A pinned older run is shown while the machine runs another one.
    newerRunId:
      pinnedRunId !== null && current !== null && current.id !== pinnedRunId && isActive(current)
        ? current.id
        : null,
  };
}

export type RunDetail = ReturnType<typeof useRunDetail>;
