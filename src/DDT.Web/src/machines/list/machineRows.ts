// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { MachineSummary } from "@/machines/machines";
import type { MachineActionState } from "@/machines/useMachineActions";

import type { StrayOffer } from "./strays";

// What the table and the phone list need besides the machines themselves.
export interface MachineRowsProps {
  machines: MachineSummary[];
  now: number;
  canDecide: boolean;
  actions: MachineActionState;
  // Keyed by the id of the machine that offers to remove the strays.
  strays: Map<string, StrayOffer>;
  // A row's classes from useLiveMarks.
  mark: (id: string) => string;
}
