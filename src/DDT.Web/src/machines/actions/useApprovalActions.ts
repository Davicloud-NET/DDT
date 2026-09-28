// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, type QueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { hasAnswerErrors, type InputAnswer } from "@/inputs/inputs";
import { ApiError } from "@/lib/api";
import { approvalPlan, type ApprovalPlan } from "@/machines/approval";
import {
  approveMachine,
  rejectMachine,
  upsertMachine,
  type MachineSummary,
} from "@/machines/machines";
import { isRuleChoice, sequenceResolutionQuery } from "@/rules/rules";
import { sequencesQuery } from "@/sequences/sequences";

import type { ApprovalRequest } from "./machineRequests";

// Approves and rejects machines. An approval that also runs a rule's sequence is confirmed first.
export function useApprovalActions(queryClient: QueryClient, refresh: () => void) {
  const [approveOn, setApproveOn] = useState<ApprovalRequest | null>(null);

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
    // If the server refused an answer, the operator corrects it. Any other error may mean someone else decided first.
    onError: (error) => {
      if (!hasAnswerErrors(error instanceof ApiError ? error : null)) {
        refresh();
      }
    },
  });

  // Reads the rules' current choice before an approval, because the approval runs that sequence.
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

  return { decide, approveWithPlan, prepareApproval, approveOn, setApproveOn };
}
