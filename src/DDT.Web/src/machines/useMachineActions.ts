// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { endDeployment } from "@/deployments/deployments";
import { hasAnswerErrors, type InputAnswer } from "@/inputs/inputs";
import { ApiError } from "@/lib/api";
import { approvalPlan, type ApprovalPlan } from "@/machines/approval";
import {
  approveMachine,
  isStray,
  machinesQuery,
  rejectMachine,
  removeMachine,
  removeMachines,
  removeWaitingFrom,
  upsertMachine,
  type MachineSummary,
} from "@/machines/machines";
import { isRuleChoice, sequenceResolutionQuery } from "@/rules/rules";
import { sequencesQuery } from "@/sequences/sequences";

// The deployment the stop confirmation was opened for.
export interface StopRequest {
  machineId: string;
  deploymentId: string;
}

// The approval the confirmation was opened for.
export interface ApprovalRequest {
  machineId: string;
  plan: ApprovalPlan;
}

// What an operator does to machines, shared by every page that shows them. One set serves all machines on a
// page, so one action runs at a time.
export function useMachineActions() {
  const queryClient = useQueryClient();

  const [assignTo, setAssignTo] = useState<string | null>(null);
  const [stopOn, setStopOn] = useState<StopRequest | null>(null);
  const [approveOn, setApproveOn] = useState<ApprovalRequest | null>(null);

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: machinesQuery.queryKey });
  };

  const decide = useMutation({
    mutationFn: ({ id, approve }: { id: string; approve: boolean }) =>
      approve ? approveMachine(id) : rejectMachine(id),
    onSuccess: (machine) => {
      upsertMachine(queryClient, machine);
    },
    // Usually someone else decided first, or the machine registered again. Show what is stored now.
    onError: refresh,
  });

  const approveWithPlan = useMutation({
    mutationFn: ({
      id,
      plan,
      allowSecureBootMismatch = false,
      answers = [],
    }: {
      id: string;
      plan: ApprovalPlan;
      allowSecureBootMismatch?: boolean;
      answers?: InputAnswer[];
    }) => approveMachine(id, plan.expectedSequenceId, allowSecureBootMismatch, answers),
    onSuccess: (machine) => {
      upsertMachine(queryClient, machine);
      setApproveOn(null);
    },
    // A refused answer is the operator's to correct; anything else may be someone else deciding first.
    onError: (error) => {
      if (!hasAnswerErrors(error instanceof ApiError ? error : null)) {
        refresh();
      }
    },
  });

  // Reads fresh what the rules choose before an approval, because the approval runs that sequence.
  const prepareApproval = useMutation({
    mutationFn: async (machine: MachineSummary) => {
      if (machine.signedInBy !== null) {
        return null;
      }

      const resolution = await queryClient.query({
        ...sequenceResolutionQuery(machine.id),
        staleTime: 0,
      });
      const sequences = isRuleChoice(resolution)
        ? await queryClient.query({ ...sequencesQuery, staleTime: 0 })
        : [];

      return approvalPlan(machine, resolution, sequences);
    },
    onSuccess: (plan, machine) => {
      if (plan === null) {
        decide.mutate({ id: machine.id, approve: true });
      } else {
        approveWithPlan.reset();
        setApproveOn({ machineId: machine.id, plan });
      }
    },
  });

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

// The confirmation is for the deployment that was running when Stop was clicked. Once that one ended, the
// dialog closes, so a late confirm cannot stop another deployment on the same machine.
export function isStopRequested(stopOn: StopRequest | null, machine: MachineSummary): boolean {
  return (
    stopOn !== null &&
    stopOn.machineId === machine.id &&
    machine.deployment?.id === stopOn.deploymentId &&
    machine.deployment.state === "Running"
  );
}

// The plan holds while the machine waits. Once someone else decided, the dialog closes.
export function approvalRequested(
  approveOn: ApprovalRequest | null,
  machine: MachineSummary,
): ApprovalPlan | null {
  return approveOn !== null && approveOn.machineId === machine.id && machine.state === "Pending"
    ? approveOn.plan
    : null;
}
