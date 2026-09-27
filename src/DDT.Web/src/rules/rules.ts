// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { queryOptions } from "@tanstack/react-query";

import { apiDelete, apiGet, apiPost, apiPut, type ApiProblem } from "@/lib/api";
import { serverText, type ServerArguments } from "@/lib/serverText";
import { formatMac, type HardwareModelCount, type MachineSummary } from "@/machines/machines";
import { matchingMachines } from "@/packages/packages";

// Where a machine's sequence comes from, first match first: an assignment on the web, a choice at the machine,
// a rule for one of its MAC addresses, a rule for its model.
export type SequenceResolutionSource = "None" | "Assigned" | "Console" | "MacRule" | "ModelRule";

// The sequence a machine would get and why. A rule only chooses: the machine still needs an approval or a
// sign-in. problemCount above zero means the chosen sequence cannot run until it is fixed. explanation is the
// server's English; resolutionText says it in the person's language.
export interface MachineSequenceResolution {
  source: SequenceResolutionSource;
  sequenceId: string | null;
  sequenceName: string | null;
  ruleId: string | null;
  problemCount: number;
  explanation: string;
  explanationCode?: string | null;
  explanationArgs?: ServerArguments | null;
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

export type AssignmentRuleKind = "Mac" | "Model";

// mac is set for a MAC rule, twelve hex digits; manufacturer and model for a model rule, where a null
// manufacturer matches any and a model ending in * matches every model that starts with the text before it.
export interface AssignmentRuleView {
  id: string;
  kind: AssignmentRuleKind;
  mac: string | null;
  manufacturer: string | null;
  model: string | null;
  sequenceId: string;
  sequenceName: string;
  description: string | null;
  updatedUtc: string;
  updatedBy: string | null;
}

export const rulesQuery = queryOptions({
  queryKey: ["rules"],
  queryFn: () => apiGet<AssignmentRuleView[]>("/api/rules"),
});

// "MAC 00:15:5D:01:02:03" or "model Dell Inc. Latitude 7440", to put in a sentence.
export function describeRule(rule: AssignmentRuleView): string {
  if (rule.kind === "Mac") {
    const mac = formatMac(rule.mac ?? "");

    return t`MAC ${mac}`;
  }

  const model = ruleTarget(rule);

  return t`model ${model}`;
}

// What the rule matches, without saying which kind of rule it is: a MAC address, or a maker and model.
export function ruleTarget(rule: AssignmentRuleView): string {
  if (rule.kind === "Mac") {
    return formatMac(rule.mac ?? "");
  }

  return `${rule.manufacturer === null ? "" : `${rule.manufacturer} `}${rule.model ?? ""}`;
}

export function isRuleChoice(resolution: MachineSequenceResolution): boolean {
  return (
    (resolution.source === "MacRule" || resolution.source === "ModelRule") &&
    resolution.sequenceId !== null
  );
}

// Only the fields of the rule's kind are read: mac for a MAC rule, manufacturer and model for a model rule.
export interface SaveAssignmentRuleRequest {
  kind: AssignmentRuleKind;
  mac: string | null;
  manufacturer: string | null;
  model: string | null;
  sequenceId: string;
  description: string | null;
}

// What a rule's row edits, as typed. Its kind never changes.
export interface RuleEdit {
  mac: string;
  manufacturer: string;
  model: string;
  sequenceId: string;
  description: string;
}

export type RuleField = keyof RuleEdit;

export function createRule(request: SaveAssignmentRuleRequest): Promise<AssignmentRuleView> {
  return apiPost<AssignmentRuleView>("/api/rules", request);
}

// The last save wins: the server keeps no revision of a rule.
export function updateRule(
  id: string,
  request: SaveAssignmentRuleRequest,
): Promise<AssignmentRuleView> {
  return apiPut<AssignmentRuleView>(`/api/rules/${id}`, request);
}

export function deleteRule(id: string): Promise<void> {
  return apiDelete(`/api/rules/${id}`);
}

export function editOf(rule: AssignmentRuleView): RuleEdit {
  return {
    mac: rule.mac === null ? "" : formatMac(rule.mac),
    manufacturer: rule.manufacturer ?? "",
    model: rule.model ?? "",
    sequenceId: rule.sequenceId,
    description: rule.description ?? "",
  };
}

export function emptyEdit(sequenceId: string): RuleEdit {
  return { mac: "", manufacturer: "", model: "", sequenceId, description: "" };
}

function orNull(text: string): string | null {
  return text.trim() === "" ? null : text;
}

export function requestOf(kind: AssignmentRuleKind, edit: RuleEdit): SaveAssignmentRuleRequest {
  return {
    kind,
    mac: kind === "Mac" ? edit.mac : null,
    manufacturer: kind === "Model" ? orNull(edit.manufacturer) : null,
    model: kind === "Model" ? edit.model : null,
    sequenceId: edit.sequenceId,
    description: orNull(edit.description),
  };
}

// Where the server's refusal of a rule belongs. A 400 names its fields; a 409 says another rule matches the same
// machines, which belongs to the MAC or the model.
export function refusalMessages(
  kind: AssignmentRuleKind,
  refusal: { message: string; problem: ApiProblem | null } | null,
  field: RuleField,
): string[] {
  if (refusal === null) {
    return [];
  }

  const errors = refusal.problem?.errors ?? {};

  if (Object.keys(errors).length > 0) {
    return errors[field] ?? [];
  }

  return field === (kind === "Mac" ? "mac" : "model") ? [refusal.message] : [];
}

// How many registered machines the rule matches, for information: the server decides when a machine asks.
export function ruleMatches(
  rule: AssignmentRuleView,
  machines: readonly MachineSummary[],
  models: readonly HardwareModelCount[],
): number {
  if (rule.kind === "Model") {
    return matchingMachines([{ manufacturer: rule.manufacturer, model: rule.model ?? "" }], models);
  }

  return machines.filter(
    (machine) => machine.primaryMac === rule.mac || machine.macAddresses.includes(rule.mac ?? ""),
  ).length;
}

export function ruleDeletionConsequence(rule: AssignmentRuleView): string {
  const target = ruleTarget(rule);
  const sequence = rule.sequenceName;

  return rule.kind === "Mac"
    ? t`The machine with MAC ${target} no longer gets ${sequence} chosen for it; another rule or an operator chooses instead. A machine that already has a run keeps it.`
    : t`Machines of model ${target} no longer get ${sequence} chosen for them; another rule or an operator chooses instead. Machines that already have a run keep it.`;
}
