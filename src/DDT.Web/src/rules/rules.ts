// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions } from "@tanstack/react-query";

import { apiGet } from "@/lib/api";
import { formatMac } from "@/machines/machines";

// Where a machine's sequence comes from, first match first: an assignment on the web, a choice at the machine,
// a rule for one of its MAC addresses, a rule for its model.
export type SequenceResolutionSource = "None" | "Assigned" | "Console" | "MacRule" | "ModelRule";

// The sequence a machine would get and why. A rule only chooses: the machine still needs an approval or a
// sign-in. problemCount above zero means the chosen sequence cannot run until it is fixed.
export interface MachineSequenceResolution {
  source: SequenceResolutionSource;
  sequenceId: string | null;
  sequenceName: string | null;
  ruleId: string | null;
  problemCount: number;
  explanation: string;
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
    return `MAC ${formatMac(rule.mac ?? "")}`;
  }

  return `model ${rule.manufacturer === null ? "" : `${rule.manufacturer} `}${rule.model ?? ""}`;
}

export function isRuleChoice(resolution: MachineSequenceResolution): boolean {
  return (
    (resolution.source === "MacRule" || resolution.source === "ModelRule") &&
    resolution.sequenceId !== null
  );
}
