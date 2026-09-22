// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// The search parameters of a machine's page. run pins a run from its history; without it the page follows the
// machine's current run.
export interface MachineSearch {
  run?: string;
}

export function machineSearch(search: Record<string, unknown>): MachineSearch {
  const run = search.run;

  return typeof run === "string" && run !== "" ? { run } : {};
}
