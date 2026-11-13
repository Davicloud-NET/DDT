// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";

import { webInputs } from "@/inputs/inputs";
import { useDocumentInputs } from "@/inputs/useDocumentInputs";
import type { MachineSummary } from "@/machines/machines";
import { isRuleChoice, sequenceResolutionQuery, valuesComputerName } from "@/rules/rules";
import { canRun, sequenceQuery, sequencesQuery } from "@/sequences/sequences";

// The sequence the assign dialog assigns, with its inputs asked on the web and their starting values.
export function useSequenceChoice(machine: MachineSummary) {
  const sequences = useQuery(sequencesQuery);
  const resolution = useQuery(sequenceResolutionQuery(machine.id));
  const [chosenId, setChosenId] = useState<string | null>(null);

  const list = sequences.data ?? [];
  const runnable = list.filter(canRun);
  const ruleChoice =
    resolution.data !== undefined && isRuleChoice(resolution.data) ? resolution.data : null;
  // The rule's choice comes first, until the operator chooses.
  const sequence =
    runnable.find((candidate) => candidate.id === chosenId) ??
    runnable.find((candidate) => candidate.id === ruleChoice?.sequenceId) ??
    runnable[0] ??
    null;
  // The resolution already has the inputs of the sequence the rules choose for the machine, as the server asks them.
  // Another sequence's inputs are read from its document.
  const resolved = sequence !== null && sequence.id === resolution.data?.sequenceId;
  const document = useQuery({
    ...sequenceQuery(sequence?.id ?? ""),
    enabled: sequence !== null && !resolution.isPending && !resolved,
  });
  const declared = useDocumentInputs(resolved ? null : document.data?.definition.inputs);
  // A name from the machine's values, such as a rule's name pattern, counts as a name. One typed here still wins over
  // it.
  const valuesName =
    resolution.data === undefined || sequence === null
      ? null
      : valuesComputerName(resolution.data, resolved);

  return {
    sequences,
    list,
    runnable,
    ruleChoice,
    sequence,
    setChosenId,
    document,
    inputs: resolved ? webInputs(resolution.data?.inputs) : declared.inputs,
    defaults: resolved ? (resolution.data?.inputDefaults ?? []) : [],
    inputsKnown: resolved || (document.isSuccess && declared.known) || document.isError,
    valuesName,
  };
}
