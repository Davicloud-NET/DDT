// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions } from "@tanstack/react-query";

import { apiGet } from "@/lib/api";

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

export function isRuleChoice(resolution: MachineSequenceResolution): boolean {
  return (
    (resolution.source === "MacRule" || resolution.source === "ModelRule") &&
    resolution.sequenceId !== null
  );
}
