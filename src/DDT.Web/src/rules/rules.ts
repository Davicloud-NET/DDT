// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { queryOptions, type QueryClient } from "@tanstack/react-query";

import { formattingLocale } from "@/i18n/i18n";
import { apiDelete, apiErrorFrom, apiFetch, apiGet, apiPost, apiPut } from "@/lib/api";
import { serverText, type ServerArguments } from "@/lib/serverText";
import type { ConditionNode, InputDeclaration, SequenceProblem } from "@/sequences/sequences";
import {
  editedValues,
  namedValues,
  type EditedValue,
  type NamedValue,
  type ResolvedValue,
} from "@/values/values";

// Where a machine's sequence comes from, first match first: an assignment on the web, a choice at the machine, the
// first rule of the ordered list that matches the machine and chooses a sequence. MacRule and ModelRule are what the
// assignment rules before the ordered list said; a server of this version no longer says them.
export type SequenceResolutionSource =
  "None" | "Assigned" | "Console" | "MacRule" | "ModelRule" | "Rule";

// The sequence a machine would get and why. A rule only chooses: the machine still needs an approval or a
// sign-in. problemCount above zero means the chosen sequence cannot run until it is fixed. explanation is the
// server's English; resolutionText says it in the person's language.
//
// The rest previews what a run of that sequence would start with, from servers that send it. matchedRuleIds are the
// rules that match the machine, top first. values are the values the run would have, each with its source; a run
// that is running shows the values it started with. inputs are the chosen sequence's inputs, and inputDefaults what
// their questions start with. valueProblems would keep the run from starting as things are now; a problem's field
// is the value's or the input's name.
export interface MachineSequenceResolution {
  source: SequenceResolutionSource;
  sequenceId: string | null;
  sequenceName: string | null;
  ruleId: string | null;
  problemCount: number;
  explanation: string;
  explanationCode?: string | null;
  explanationArgs?: ServerArguments | null;
  matchedRuleIds?: string[] | null;
  values?: ResolvedValue[] | null;
  inputs?: InputDeclaration[] | null;
  inputDefaults?: ResolvedValue[] | null;
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

// The computer name a run gets from the machine's values when none is given, such as a rule's
// PC-{{SerialNumber|alnum|right:8}}, as the server takes it: the ComputerName value that is used, when it is one Windows
// takes. Null when nothing gives one. The preview is of the sequence the resolution names, so for another sequence
// (sameSequence false) only what the machine, the rules and the machine roles give counts, not that sequence's default.
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

// One rule of the ordered list, position counting from 0 at the top. A rule whose when holds for a machine, or that
// has none, chooses sequenceId, sets values and gives the machine roles roleIds; the first rule to choose a sequence
// or set a value wins it. Rules never authorize a machine. problems keep the rule from matching until they are fixed;
// each names its field within the rule, such as when.parts[0].value, values[1].name or roleIds[0].
// matchingMachines is how many known machines the rule matches.
export interface RuleView {
  id: string;
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
  problems: SequenceProblem[];
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

// The list in the order of ids, each numbered by its place again. Ids the list does not have are left out, and rules
// the ids do not name keep their order after the rest.
export function inOrder(list: readonly RuleView[], ids: readonly string[]): RuleView[] {
  const named = ids.flatMap((id) => list.filter((rule) => rule.id === id));
  const rest = list.filter((rule) => !ids.includes(rule.id));

  return [...named, ...rest].map((rule, position) =>
    rule.position === position ? rule : { ...rule, position },
  );
}

// The order after the rule moves by offset places, such as -1 for up; null where it cannot go further.
export function movedBy(list: readonly RuleView[], id: string, offset: number): string[] | null {
  const ids = list.map((rule) => rule.id);
  const from = ids.indexOf(id);
  const to = from + offset;

  if (from < 0 || to < 0 || to >= ids.length) {
    return null;
  }

  ids.splice(from, 1);
  ids.splice(to, 0, id);

  return ids;
}

// The order after the rules were dropped before or after another; null where nothing moves.
export function droppedAt(
  list: readonly RuleView[],
  moving: readonly string[],
  target: string,
  position: "before" | "after",
): string[] | null {
  const ids = list.map((rule) => rule.id);
  const staying = ids.filter((id) => !moving.includes(id));
  const at = staying.indexOf(target);

  if (at < 0) {
    return null;
  }

  const cut = position === "before" ? at : at + 1;
  const order = [
    ...staying.slice(0, cut),
    ...moving.filter((id) => ids.includes(id)),
    ...staying.slice(cut),
  ];

  return order.every((id, index) => id === ids[index]) ? null : order;
}

// "Rule 2, Berlin office", to begin a sentence or stand alone, and "rule 2, Berlin office" inside one.
export function ruleName(rule: Pick<RuleView, "position" | "name">): string {
  const number = rule.position + 1;
  const name = rule.name;

  return t`Rule ${number}, ${name}`;
}

export function ruleNameInside(rule: Pick<RuleView, "position" | "name">): string {
  const number = rule.position + 1;
  const name = rule.name;

  return t`rule ${number}, ${name}`;
}

// Rules named in a sentence, the first as it begins one: "Rule 2, Berlin office, and rule 4, Latitude laptops".
export function ruleNames(rules: readonly Pick<RuleView, "position" | "name">[]): string {
  const [first, ...rest] = rules;

  if (first === undefined) {
    return "";
  }

  return new Intl.ListFormat(formattingLocale(), { type: "conjunction" }).format([
    ruleName(first),
    ...rest.map(ruleNameInside),
  ]);
}

// What a rule does to the machines it matches, a few words each: "Chooses Kiosk", "Sets TimeZone", "Gives Office
// PC". A role that is gone is left out; the rule's problems say so.
export function ruleEffects(
  rule: Pick<RuleView, "sequenceName" | "values" | "roleIds">,
  roles: readonly { id: string; name: string }[],
): string[] {
  const effects: string[] = [];
  const sequence = rule.sequenceName;

  if (sequence !== null) {
    effects.push(t`Chooses ${sequence}`);
  }

  for (const value of rule.values) {
    const name = value.name;

    effects.push(t`Sets ${name}`);
  }

  for (const id of rule.roleIds) {
    const role = roles.find((candidate) => candidate.id === id)?.name;

    if (role !== undefined) {
      effects.push(t`Gives ${role}`);
    }
  }

  return effects;
}

// What a rule's drawer edits, as typed.
export interface RuleEdit {
  name: string;
  description: string;
  enabled: boolean;
  when: ConditionNode | null;
  sequenceId: string | null;
  values: EditedValue[];
  roleIds: string[];
}

export function editOf(rule: RuleView): RuleEdit {
  return {
    name: rule.name,
    description: rule.description ?? "",
    enabled: rule.enabled,
    when: rule.when,
    sequenceId: rule.sequenceId,
    values: editedValues(rule.values),
    roleIds: rule.roleIds,
  };
}

export function emptyEdit(): RuleEdit {
  return {
    name: "",
    description: "",
    enabled: true,
    when: null,
    sequenceId: null,
    values: [],
    roleIds: [],
  };
}

export function requestOf(revision: number, edit: RuleEdit): SaveRuleRequest {
  const description = edit.description.trim();

  return {
    revision,
    name: edit.name.trim(),
    description: description === "" ? null : description,
    enabled: edit.enabled,
    when: edit.when,
    sequenceId: edit.sequenceId,
    values: namedValues(edit.values),
    roleIds: edit.roleIds,
  };
}

// A value's source inside a sentence, such as "rule 2" or "the machine role Office PC".
export function ruleValueSource(value: ResolvedValue, rules: readonly RuleView[]): string {
  const name = value.sourceName ?? "";

  switch (value.source) {
    case "Rule": {
      const rule = rules.find((candidate) => candidate.id === value.sourceId);

      if (rule === undefined) {
        return t`the rule ${name}`;
      }

      const number = rule.position + 1;

      return t`rule ${number}`;
    }
    case "Role":
      return t`the machine role ${name}`;
    case "Input":
      return t`an answer to the sequence's question`;
    case "Machine":
      return t`the machine's own value`;
    case "SequenceDefault":
      return t`the sequence's default`;
    case "DeploymentDefault":
      return t`the deployment defaults`;
    case "Fact":
      return t`a fact of the machine`;
    case "Step":
      return t`the step ${name}`;
  }
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
