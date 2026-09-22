// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions } from "@tanstack/react-query";

import { apiGet } from "@/lib/api";

export type SequencePhase = "WindowsPE" | "Windows";

// A sequence with problemCount above zero is kept as a draft and cannot run. The facts let a dialog say what
// running it does without loading the whole document. needsComputerName: the sequence joins the domain.
export interface SequenceSummary {
  id: string;
  name: string;
  description: string | null;
  revision: number;
  stepCount: number;
  problemCount: number;
  warningCount: number;
  erasesDisk: boolean;
  needsComputerName: boolean;
  continuesInWindows: boolean;
  updatedUtc: string;
  updatedBy: string | null;
}

export const sequencesQuery = queryOptions({
  queryKey: ["sequences"],
  queryFn: () => apiGet<SequenceSummary[]>("/api/sequences"),
});

// The server refuses to assign or start a sequence with problems.
export function canRun(sequence: SequenceSummary): boolean {
  return sequence.problemCount === 0;
}
