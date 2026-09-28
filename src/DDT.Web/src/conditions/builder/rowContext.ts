// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ConditionChange } from "@/sequences/flow/conditionTree";
import type { Findings } from "@/sequences/problems";

import type { Subject } from "../conditionSubjects";

// What every row of the condition builder reads, handed down through the nested groups.
export interface RowContext {
  subjects: readonly Subject[];
  place: (path: readonly number[]) => string;
  findings: Findings;
  onChange: (path: readonly number[], change: ConditionChange) => void;
  remove: (path: readonly number[]) => void;
  locked: boolean;
  // Each test's number from 1 in reading order, by its path.
  numbers: ReadonlyMap<string, number>;
}
