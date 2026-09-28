// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";

import { webInputs } from "@/inputs/inputs";
import type { MachineSummary } from "@/machines/machines";
import { isRuleChoice, sequenceResolutionQuery, valuesComputerName } from "@/rules/rules";
import { canRun, sequenceQuery, sequencesQuery } from "@/sequences/sequences";

// The sequence the assign dialog assigns, with the inputs it asks on the web and what they start with.
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
  // The inputs of the sequence the machine would get come with what it would get; another sequence's are read from it.
  const resolved = sequence !== null && sequence.id === resolution.data?.sequenceId;
  const document = useQuery({
    ...sequenceQuery(sequence?.id ?? ""),
    enabled: sequence !== null && !resolution.isPending && !resolved,
  });
  // A name the machine's values give, such as a rule's pattern, is as good as one typed here, which beats it.
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
    inputs: webInputs(
      resolved ? resolution.data?.inputs : (document.data?.definition.inputs ?? null),
    ),
    defaults: resolved ? (resolution.data?.inputDefaults ?? []) : [],
    inputsKnown: resolved || document.isSuccess || document.isError,
    valuesName,
  };
}
