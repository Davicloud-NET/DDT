// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import {
  assignSequence,
  deploymentOptionsQuery,
  type AssignSequenceRequest,
} from "@/deployments/deployments";
import { hasAnswerErrors } from "@/inputs/inputs";
import { useAnswers } from "@/inputs/useAnswers";
import { ApiError } from "@/lib/api";
import { useNow } from "@/lib/useNow";
import { upsertMachine, type MachineSummary } from "@/machines/machines";
import { secureBootRisk } from "@/machines/secureBoot";

import { nameRequiredText } from "./assignText";
import { useSequenceChoice } from "./useSequenceChoice";

// The assign dialog's state: the sequence, the computer name, the answers and the Secure Boot allowance, what keeps
// the assignment from being sent, and the assignment, whose answer replaces the machine in the list.
export function useAssignForm(machine: MachineSummary, onClose: () => void) {
  const queryClient = useQueryClient();
  const choice = useSequenceChoice(machine);
  const options = useQuery(deploymentOptionsQuery);
  // The server decides with its clock whether the machine waits at the prompt, so the dialog does too.
  const now = useNow(5_000) + (options.data?.serverClockOffsetMs ?? 0);
  const [computerName, setComputerName] = useState(machine.assignedName ?? "");
  const [nameProblem, setNameProblem] = useState<string | null>(null);
  // The sequence and image the allowance was given for, so another sequence, or another image written by the same
  // one after a live update, asks again.
  const [allowedFor, setAllowedFor] = useState<string | null>(null);

  const assign = useMutation({
    mutationFn: (request: AssignSequenceRequest) => assignSequence(machine.id, request),
    onSuccess: (updated) => {
      upsertMachine(queryClient, updated);
      onClose();
    },
  });
  const answers = useAnswers(choice.inputs, choice.defaults);

  const { sequence } = choice;
  const erases = sequence?.erasesDisk === true;
  const risk = secureBootRisk(machine, sequence);
  const allowanceKey = sequence === null ? null : `${sequence.id} ${sequence.rawImageName ?? ""}`;
  const allowed = risk !== null && allowedFor !== null && allowedFor === allowanceKey;
  const nameRequired =
    sequence?.needsComputerName === true &&
    machine.assignedName === null &&
    choice.valuesName === null;
  const severalDisks = machine.eligibleDiskCount !== null && machine.eligibleDiskCount > 1;
  const serverNameProblem =
    assign.error instanceof ApiError
      ? (assign.error.problem?.errors?.computerName?.[0] ?? null)
      : null;
  const refused = assign.error instanceof ApiError ? assign.error : null;

  function submit() {
    if (sequence === null) {
      return;
    }

    const name = computerName.trim();

    const missingName = nameRequired && name === "";

    setNameProblem(missingName ? nameRequiredText(sequence) : null);

    const given = answers.collect();

    if (missingName || given === null) {
      return;
    }

    assign.mutate({
      sequenceId: sequence.id,
      computerName: name === "" ? null : name,
      ...(allowed ? { allowSecureBootMismatch: true } : {}),
      ...(given.length > 0 ? { answers: given } : {}),
    });
  }

  return {
    ...choice,
    options,
    now,
    assign,
    answers,
    computerName,
    setComputerName,
    nameRequired,
    fieldProblem: nameProblem ?? serverNameProblem,
    erases,
    severalDisks,
    risk,
    allowed,
    refused,
    error:
      assign.isError && serverNameProblem === null && !hasAnswerErrors(refused)
        ? assign.error.message
        : null,
    // Without the settings the dialog cannot say what the assignment does, so it does not offer it.
    canSubmit:
      sequence !== null &&
      !(erases && severalDisks) &&
      !(risk?.required === true && !allowed) &&
      !assign.isPending &&
      options.data !== undefined &&
      !choice.sequences.isPending &&
      choice.inputsKnown,
    choose: (id: string | null) => {
      choice.setChosenId(id);
      setAllowedFor(null);
    },
    allow: (selected: boolean) => {
      setAllowedFor(selected ? allowanceKey : null);
    },
    submit,
  };
}

export type AssignFormState = ReturnType<typeof useAssignForm>;
