// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { isMachineFilter, type MachineFilter } from "./machineView";

// The search parameters of a machine's page. run pins a run from its history; without it the page follows the
// machine's current run.
export interface MachineSearch {
  run?: string;
}

export function machineSearch(search: Record<string, unknown>): MachineSearch {
  const run = search.run;

  return typeof run === "string" && run !== "" ? { run } : {};
}

// The search parameters of the machine list: the state shown, the search text, and the machine whose details
// are open beside the list. They live in the address, so a view can be shared and survives a reload.
export interface MachinesSearch {
  state?: MachineFilter;
  q?: string;
  selected?: string;
}

export function machinesSearch(search: Record<string, unknown>): MachinesSearch {
  return {
    ...(isMachineFilter(search.state) && search.state !== "all" ? { state: search.state } : {}),
    ...(typeof search.q === "string" && search.q !== "" ? { q: search.q } : {}),
    ...(typeof search.selected === "string" && search.selected !== ""
      ? { selected: search.selected }
      : {}),
  };
}
