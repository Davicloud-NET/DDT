// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { queryOptions, type QueryClient } from "@tanstack/react-query";

import { apiDelete, apiErrorFrom, apiFetch, apiGet, apiPost, apiPut } from "@/lib/api";
import { serverText, type ServerArguments } from "@/lib/serverText";
import type { ConditionNode } from "@/sequences/sequenceConditions";
import type { InputDeclaration, SequenceProblem } from "@/sequences/sequences";
import type { NamedValue, ResolvedValue } from "@/values/values";

// Where a machine's sequence comes from, first match first: an assignment on the web, a choice at the machine, the
// first matching rule that chooses one. Only older servers say MacRule and ModelRule.
export type SequenceResolutionSource =
  "None" | "Assigned" | "Console" | "MacRule" | "ModelRule" | "Rule";

// The sequence a machine would get and why. A rule only chooses: the machine still needs an approval or a sign-in.
export interface MachineSequenceResolution {
  source: SequenceResolutionSource;
  sequenceId: string | null;
  sequenceName: string | null;
  ruleId: string | null;
  // Above zero, the chosen sequence cannot run until it is fixed.
  problemCount: number;
  // The server's English; resolutionText says it in the person's language.
  explanation: string;
  explanationCode?: string | null;
  explanationArgs?: ServerArguments | null;
  // The rest previews what a run would start with, from servers that send it. The matching rules, top first.
  matchedRuleIds?: string[] | null;
  // Each with its source; a running run shows the values it started with.
  values?: ResolvedValue[] | null;
  inputs?: InputDeclaration[] | null;
  // What the inputs' questions start with.
  inputDefaults?: ResolvedValue[] | null;
  // What would keep the run from starting now, each field the value's or the input's name.
  valueProblems?: SequenceProblem[] | null;
}

export function resolutionText(resolution: MachineSequenceResolution): string {
  return serverText(resolution.explanationCode, resolution.explanationArgs, resolution.explanation);
}

// The root of every machine's resolution, which a change of the rules or the sequences makes stale.
export const sequenceResolutionsKey = ["machine-sequence"] as const;

export function sequenceResolutionQuery(machineId: string) {
  return queryOptions({
    queryKey: [...sequenceResolutionsKey, machineId],
    queryFn: () => apiGet<MachineSequenceResolution>(`/api/machines/${machineId}/sequence`),
  });
}

// Whether a rule chose the machine's sequence, rather than someone on the web or at the machine.
export function isRuleChoice(resolution: MachineSequenceResolution): boolean {
  return (
    (resolution.source === "Rule" ||
      resolution.source === "MacRule" ||
      resolution.source === "ModelRule") &&
    resolution.sequenceId !== null
  );
}

// The ComputerName a run gets from its values when none is given, or null where there is none or it has a problem.
// For a sequence other than the previewed one (sameSequence false), only machine, rule and role values count.
export function valuesComputerName(
  resolution: MachineSequenceResolution,
  sameSequence = true,
): string | null {
  const computerName = (name: string | null) => name?.toLowerCase() === "computername";

  if ((resolution.valueProblems ?? []).some((problem) => computerName(problem.field))) {
    return null;
  }

  const used = (resolution.values ?? []).find(
    (value) => !value.overridden && computerName(value.name),
  );
  const name = used?.value?.trim() ?? "";

  if (used === undefined || name === "") {
    return null;
  }

  return sameSequence ||
    used.source === "Machine" ||
    used.source === "Rule" ||
    used.source === "Role"
    ? name
    : null;
}

// The rule that chose a machine's sequence, as the subject of a sentence and inside one: "Rule 2" and "rule 2".
export function ruleChoiceWords(resolution: MachineSequenceResolution): {
  subject: string;
  inside: string;
} {
  const number = resolution.explanationArgs?.number;

  switch (resolution.source) {
    case "MacRule":
      return { subject: t`A rule for its MAC address`, inside: t`a rule for its MAC address` };
    case "ModelRule":
      return { subject: t`A rule for its model`, inside: t`a rule for its model` };
    default:
      return typeof number === "number"
        ? { subject: t`Rule ${number}`, inside: t`rule ${number}` }
        : { subject: t`A rule`, inside: t`a rule` };
  }
}

// One rule of the ordered list. Where its when holds, or it has none, it chooses sequenceId, sets values and gives
// roleIds; the first rule to choose a sequence or set a value wins it. Rules never authorize a machine.
export interface RuleView {
  id: string;
  // From 0 at the top.
  position: number;
  name: string;
  description: string | null;
  enabled: boolean;
  when: ConditionNode | null;
  sequenceId: string | null;
  sequenceName: string | null;
  values: NamedValue[];
  roleIds: string[];
  revision: number;
  // Keep the rule from matching until fixed; each names its field, such as when.parts[0].value or roleIds[0].
  problems: SequenceProblem[];
  // How many known machines the rule matches.
  matchingMachines: number;
  updatedUtc: string;
  updatedBy: string | null;
}

// Creates a rule at the bottom of the list, or saves one. revision is the one the page last read, and a save over a
// newer one is refused with the rule as it is now; a new rule has none to name.
export interface SaveRuleRequest {
  revision: number;
  name: string;
  description: string | null;
  enabled: boolean;
  when: ConditionNode | null;
  sequenceId: string | null;
  values: NamedValue[];
  roleIds: string[];
}

export const rulesQuery = queryOptions({
  queryKey: ["rules"],
  queryFn: () => apiGet<RuleView[]>("/api/rules"),
});

export function createRule(request: SaveRuleRequest): Promise<RuleView> {
  return apiPost<RuleView>("/api/rules", request);
}

export function updateRule(id: string, request: SaveRuleRequest): Promise<RuleView> {
  return apiPut<RuleView>(`/api/rules/${id}`, request);
}

// The rules below move up a place, so the answer is the whole list.
export function deleteRule(id: string): Promise<RuleView[]> {
  return apiDelete<RuleView[]>(`/api/rules/${id}`);
}

// The server refused an order made before someone added, removed or moved a rule, and answered with the list as it
// is now.
export class RulesChangedMeanwhile extends Error {
  public readonly rules: RuleView[];

  public constructor(rules: RuleView[]) {
    super("The rules changed before the order arrived.");
    this.name = "RulesChangedMeanwhile";
    this.rules = rules;
  }
}

// Every rule's id in the new order, top first. The server refuses an order that does not name exactly the rules there
// are with 409 and the list as it is now, which comes as RulesChangedMeanwhile.
export async function reorderRules(ruleIds: readonly string[]): Promise<RuleView[]> {
  const response = await apiFetch("/api/rules/order", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ ruleIds }),
  });

  if (response.status === 409) {
    throw new RulesChangedMeanwhile((await response.json()) as RuleView[]);
  }

  if (!response.ok) {
    throw await apiErrorFrom(response);
  }

  return (await response.json()) as RuleView[];
}

function byPosition(a: RuleView, b: RuleView): number {
  return a.position - b.position;
}

// Puts a saved rule into the list, where its position says.
export function putRule(queryClient: QueryClient, rule: RuleView): void {
  queryClient.setQueryData(rulesQuery.queryKey, (list) =>
    list === undefined
      ? list
      : [...list.filter((existing) => existing.id !== rule.id), rule].sort(byPosition),
  );
}

// A value's name with the value it takes and where from, and the sources further down that set it too.
export interface ValueLine {
  name: string;
  used: ResolvedValue;
  overridden: ResolvedValue[];
}

export function valueLines(values: readonly ResolvedValue[]): ValueLine[] {
  return values
    .filter((value) => !value.overridden)
    .map((used) => ({
      name: used.name,
      used,
      overridden: values.filter(
        (other) => other.overridden && other.name.toLowerCase() === used.name.toLowerCase(),
      ),
    }));
}

// The search parameters of the rules page: the rule whose drawer is open, such as from a machine role's "Given by".
export interface RulesSearch {
  rule?: string;
}

export function rulesSearch(search: Record<string, unknown>): RulesSearch {
  return typeof search.rule === "string" && search.rule !== "" ? { rule: search.rule } : {};
}
