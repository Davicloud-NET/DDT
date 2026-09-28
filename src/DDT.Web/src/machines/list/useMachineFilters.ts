// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useNavigate, useSearch } from "@tanstack/react-router";

import type { MachineFilter } from "@/machines/machineView";

// A change of the list's view; undefined leaves a parameter out of the address.
export interface MachinesSearchChange {
  state?: MachineFilter;
  q?: string;
  selected?: string | undefined;
}

// The machine list's filter, search text and picked machine, which live in the address (machinesSearch).
export function useMachineFilters() {
  const search = useSearch({ from: "/shell/machines" });
  const navigate = useNavigate({ from: "/machines" });
  const filter: MachineFilter = search.state ?? "all";

  // Changes the view without a new history entry. Empty values and the default filter leave the address.
  function setSearch(next: MachinesSearchChange) {
    void navigate({
      search: (previous) => {
        const merged: Record<string, string | undefined> = { ...previous, ...next };

        return Object.fromEntries(
          Object.entries(merged).filter(
            ([key, value]) =>
              value !== undefined && value !== "" && !(key === "state" && value === "all"),
          ),
        );
      },
      replace: true,
    });
  }

  return { filter, query: search.q ?? "", selectedId: search.selected, setSearch };
}
