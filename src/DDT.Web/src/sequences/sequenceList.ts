// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@/lib/format";
import { describeRule, type AssignmentRuleView } from "@/rules/rules";

import type { SequenceDefinition, SequenceSummary } from "./sequences";

// Names are unique regardless of case, so a second sequence from the same template is numbered.
export function uniqueName(base: string, taken: readonly string[]): string {
  const names = new Set(taken.map((name) => name.trim().toLocaleUpperCase()));

  if (!names.has(base.toLocaleUpperCase())) {
    return base;
  }

  for (let number = 2; ; number++) {
    const candidate = `${base} ${String(number)}`;

    if (!names.has(candidate.toLocaleUpperCase())) {
      return candidate;
    }
  }
}

// Each sequence gets step ids of its own, even when two start from the same template.
export function withNewStepIds(definition: SequenceDefinition): SequenceDefinition {
  return {
    ...definition,
    steps: definition.steps.map((step) => ({ ...step, id: crypto.randomUUID() })),
  };
}

export function rulesChoosing(
  rules: readonly AssignmentRuleView[],
  sequenceId: string,
): AssignmentRuleView[] {
  return rules.filter((rule) => rule.sequenceId === sequenceId);
}

// What deleting a sequence does. activeRuns counts the machines it is assigned to or running on now.
export function deletionConsequence(
  sequence: SequenceSummary,
  rules: readonly AssignmentRuleView[],
  activeRuns: number,
): string {
  const sentences = [
    `${sequence.name} is deleted and can no longer be assigned or chosen at a machine.`,
  ];

  if (rules.length > 0) {
    const list = rules.map(describeRule).join(", ");

    sentences.push(
      rules.length === 1
        ? `The rule for ${list} chooses it, so the server keeps it until that rule is deleted or chooses another sequence.`
        : `The rules for ${list} choose it, so the server keeps it until those rules are deleted or choose another sequence.`,
    );
  }

  if (activeRuns === 1) {
    sentences.push("The machine it is assigned to or running on keeps the copy it got.");
  } else if (activeRuns > 1) {
    sentences.push(
      `The ${plural(activeRuns, "machine")} it is assigned to or running on keep the copy they got.`,
    );
  }

  sentences.push("Runs that already ended keep their history.");

  return sentences.join(" ");
}
