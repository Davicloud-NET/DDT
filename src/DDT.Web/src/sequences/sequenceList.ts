// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { MessageDescriptor } from "@lingui/core";
import { msg, plural, t } from "@lingui/core/macro";
import type { QueryClient } from "@tanstack/react-query";

import { isActive } from "@/deployments/deployments";
import { formatMac, type MachineSummary } from "@/machines/machines";
import type { AssignmentRuleView } from "@/rules/rules";

import {
  sequencesQuery,
  type SequenceDefinition,
  type SequenceSummary,
  type SequenceView,
} from "./sequences";

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

// What a rule matches, to put in a sentence: "MAC 00:15:5D:01:02:03" or "model Dell Inc. Latitude 7440".
export function ruleTarget(rule: AssignmentRuleView): string {
  if (rule.kind === "Mac") {
    const mac = formatMac(rule.mac ?? "");

    return t`MAC ${mac}`;
  }

  const model = [rule.manufacturer, rule.model]
    .filter((part) => part !== null && part !== "")
    .join(" ");

  return t`model ${model}`;
}

// The machines the sequence is assigned to or running on now. Each keeps the copy of the sequence it got.
export function activeRunsOf(machines: readonly MachineSummary[], sequenceId: string): number {
  return machines.filter(
    (machine) => isActive(machine.deployment) && machine.deployment?.sequenceId === sequenceId,
  ).length;
}

// Why the server refuses to delete the sequence now, or null when it deletes it: a rule still chooses it.
export function deletionBlocker(
  sequence: SequenceSummary,
  rules: readonly AssignmentRuleView[],
): string | null {
  if (rules.length === 0) {
    return null;
  }

  const name = sequence.name;
  const count = rules.length;
  const list = rules.map(ruleTarget).join(", ");

  return plural(count, {
    one: `The rule for ${list} chooses ${name}. Delete that rule or let it choose another sequence, then delete this one.`,
    other: `The rules for ${list} choose ${name}. Delete those rules or let them choose another sequence, then delete this one.`,
  });
}

// What deleting a sequence does, sentence by sentence. activeRuns counts the machines it is assigned to or running
// on now.
export function deletionConsequence(sequence: SequenceSummary, activeRuns: number): string[] {
  const name = sequence.name;
  const sentences = [t`${name} is deleted and can no longer be assigned or chosen at a machine.`];

  if (activeRuns > 0) {
    sentences.push(
      plural(activeRuns, {
        one: "The machine it is assigned to or running on keeps the copy it got and finishes with it.",
        other:
          "The # machines it is assigned to or running on keep the copy they got and finish with it.",
      }),
    );
  }

  sentences.push(t`Runs that already ended keep their history.`);

  return sentences;
}

export type SequenceFilter = "all" | "ready" | "problems";

export const sequenceFilters: {
  id: SequenceFilter;
  label: MessageDescriptor;
  tone?: "fail";
}[] = [
  { id: "all", label: msg`All` },
  { id: "ready", label: msg`Ready to run` },
  { id: "problems", label: msg`Cannot run`, tone: "fail" },
];

export function isSequenceFilter(value: unknown): value is SequenceFilter {
  return sequenceFilters.some((filter) => filter.id === value);
}

export function inFilter(sequence: SequenceSummary, filter: SequenceFilter): boolean {
  switch (filter) {
    case "all":
      return true;
    case "ready":
      return sequence.problemCount === 0;
    case "problems":
      return sequence.problemCount > 0;
  }
}

// Every word typed has to appear in the name or the description, in any case.
export function matchesSearch(sequence: SequenceSummary, query: string): boolean {
  const text = `${sequence.name} ${sequence.description ?? ""}`.toLocaleLowerCase();

  return query
    .toLocaleLowerCase()
    .split(/\s+/)
    .filter((word) => word !== "")
    .every((word) => text.includes(word));
}

// What running the sequence does to a machine, from the facts the list carries.
export function sequenceFacts(sequence: SequenceSummary): string[] {
  const image = sequence.rawImageName;

  return [
    sequence.erasesDisk ? t`Erases the disk` : t`Keeps the disk`,
    ...(image !== null ? [t`writes ${image}`] : []),
    ...(sequence.continuesInWindows ? [t`goes on in the installed Windows`] : []),
    ...(sequence.needsComputerName ? [t`needs a computer name`] : []),
  ];
}

// "2 problems, 1 warning", or null when there is neither.
export function findingCounts(problemCount: number, warningCount: number): string | null {
  const parts = [
    ...(problemCount > 0 ? [plural(problemCount, { one: "# problem", other: "# problems" })] : []),
    ...(warningCount > 0 ? [plural(warningCount, { one: "# warning", other: "# warnings" })] : []),
  ];

  return parts.length === 0 ? null : parts.join(", ");
}

const computerName = /\{\{\s*ComputerName\s*\}\}/i;

// The list's entry for a sequence, from the copy a save or a creation answered with, worked out as the server does.
// The raw image's facts depend on the library and stay as the list had them: the hub's sequenceChanged makes the
// list read the server's own entry soon after.
export function summaryOf(view: SequenceView, previous?: SequenceSummary): SequenceSummary {
  const steps = view.definition.steps;
  const writesRaw = steps.some((step) => step.kind === "writeRawImage");

  return {
    id: view.id,
    name: view.name,
    description: view.description,
    revision: view.revision,
    stepCount: steps.length,
    problemCount: view.problems.length,
    warningCount: view.warnings.length,
    erasesDisk: steps.some((step) => step.kind === "partition" || step.kind === "writeRawImage"),
    needsComputerName: steps.some(
      (step) =>
        step.kind === "joinDomain" ||
        (step.kind === "writeCloudInitSeed" &&
          [step.metaData, step.userData, step.networkConfig ?? ""].some((text) =>
            computerName.test(text),
          )),
    ),
    continuesInWindows: view.stepPhases.includes("Windows"),
    updatedUtc: view.updatedUtc,
    updatedBy: view.updatedBy,
    rawImageName: writesRaw ? (previous?.rawImageName ?? null) : null,
    rawImageBootCapability: writesRaw ? (previous?.rawImageBootCapability ?? null) : null,
    rawImageSignedUnder: writesRaw ? (previous?.rawImageSignedUnder ?? null) : null,
  };
}

// The server's order: by name regardless of case, then by id.
function compareSummaries(a: SequenceSummary, b: SequenceSummary): number {
  const left = a.name.toUpperCase();
  const right = b.name.toUpperCase();

  return left < right ? -1 : left > right ? 1 : a.id.localeCompare(b.id);
}

// Puts the answer of a creation or a save into the list, so nothing is read again for it.
export function upsertSummary(queryClient: QueryClient, view: SequenceView): void {
  queryClient.setQueryData<SequenceSummary[]>(sequencesQuery.queryKey, (list) => {
    if (list === undefined) {
      return undefined;
    }

    const previous = list.find((sequence) => sequence.id === view.id);

    // An older copy, such as a save that answered after someone else's newer one arrived, changes nothing.
    if (previous !== undefined && previous.revision > view.revision) {
      return list;
    }

    const others = list.filter((sequence) => sequence.id !== view.id);

    return [...others, summaryOf(view, previous)].sort(compareSummaries);
  });
}

export function removeSummary(queryClient: QueryClient, id: string): void {
  queryClient.setQueryData<SequenceSummary[]>(sequencesQuery.queryKey, (list) =>
    list?.filter((sequence) => sequence.id !== id),
  );
}
