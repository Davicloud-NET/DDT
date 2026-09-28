// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { endDeployment } from "@/deployments/deployments";
import {
  isStray,
  machinesQuery,
  removeMachine,
  removeMachines,
  removeWaitingFrom,
  upsertMachine,
  type MachineSummary,
} from "@/machines/machines";

import type { StopRequest } from "./actions/machineRequests";
import { useApprovalActions } from "./actions/useApprovalActions";

// What an operator does to machines, shared by every page that shows them. One set serves all machines on a
// page, so one action runs at a time.
export function useMachineActions() {
  const queryClient = useQueryClient();

  const [assignTo, setAssignTo] = useState<string | null>(null);
  const [stopOn, setStopOn] = useState<StopRequest | null>(null);

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: machinesQuery.queryKey });
  };

  const { decide, approveWithPlan, prepareApproval, approveOn, setApproveOn } = useApprovalActions(
    queryClient,
    refresh,
  );

  // The server answers a removal with no body, so the list drops the machines itself; the hub's machinesRemoved
  // does the same for everyone else looking.
  const remove = useMutation({
    mutationFn: (target: { id: string } | { address: string }) =>
      "id" in target ? removeMachine(target.id) : removeWaitingFrom(target.address),
    onSuccess: (_, target) => {
      const machines = queryClient.getQueryData<MachineSummary[]>(machinesQuery.queryKey) ?? [];

      removeMachines(
        queryClient,
        "id" in target
          ? [target.id]
          : machines
              .filter((machine) => isStray(machine) && machine.firstSeenAddress === target.address)
              .map((machine) => machine.id),
      );
    },
    onError: refresh,
  });

  const cancel = useMutation({
    mutationFn: (id: string) => endDeployment(id),
    onSuccess: (machine) => {
      upsertMachine(queryClient, machine);
    },
    onError: refresh,
  });

  const stop = useMutation({
    mutationFn: (id: string) => endDeployment(id),
    onSuccess: (machine) => {
      upsertMachine(queryClient, machine);
      setStopOn(null);
    },
    onError: refresh,
  });

  return {
    decide,
    prepareApproval,
    approveWithPlan,
    remove,
    cancel,
    stop,
    busy:
      decide.isPending ||
      prepareApproval.isPending ||
      approveWithPlan.isPending ||
      remove.isPending ||
      cancel.isPending ||
      stop.isPending,
    assignTo,
    setAssignTo,
    stopOn,
    setStopOn,
    approveOn,
    setApproveOn,
  };
}

export type MachineActionState = ReturnType<typeof useMachineActions>;
