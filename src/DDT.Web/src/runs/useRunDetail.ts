// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery, useQueryClient } from "@tanstack/react-query";

import {
  deploymentQuery,
  isActive,
  machineDeploymentsQuery,
  newerRun,
  withCurrentRun,
  withStep,
} from "@/deployments/deployments";
import { useMachineWatch } from "@/live/useMachineWatch";
import { machinesQuery } from "@/machines/machines";

// Without the live connection the page reads again this often while a run is active.
export const POLL_MS = 5_000;

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

  const machines = useQuery({
    ...machinesQuery,
    refetchInterval: (query) =>
      polling && isActive(query.state.data?.find((m) => m.id === machineId)?.deployment ?? null)
        ? POLL_MS
        : false,
  });
  const machine = machines.data?.find((candidate) => candidate.id === machineId) ?? null;

  const history = useQuery(machineDeploymentsQuery(machineId));
  const runs = withCurrentRun(history.data ?? [], machine?.deployment ?? null);
  const runId = pinnedRunId ?? machine?.deployment?.id ?? runs[0]?.id ?? null;
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

  const current = machine?.deployment ?? null;

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
