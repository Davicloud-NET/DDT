// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { MachineSummary } from "@/machines/machines";
import type { MachineActionState } from "@/machines/useMachineActions";

import type { StrayOffer } from "./strays";

// What the table and the phone's list show of the machines, beside the machines themselves.
export interface MachineRowsProps {
  machines: MachineSummary[];
  now: number;
  canDecide: boolean;
  actions: MachineActionState;
  // By the id of the machine that offers the removal.
  strays: Map<string, StrayOffer>;
  // A row's classes from useLiveMarks.
  mark: (id: string) => string;
}
