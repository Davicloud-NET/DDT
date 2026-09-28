// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useNavigate, useSearch } from "@tanstack/react-router";

import type { MachineFilter } from "@/machines/machineView";

// A change to the list's view. An undefined value leaves that parameter out of the URL.
export interface MachinesSearchChange {
  state?: MachineFilter;
  q?: string;
  selected?: string | undefined;
}

// The machine list's filter, search text and picked machine. They live in the URL (see machinesSearch).
export function useMachineFilters() {
  const search = useSearch({ from: "/shell/machines" });
  const navigate = useNavigate({ from: "/machines" });
  const filter: MachineFilter = search.state ?? "all";

  // Changes the view without adding a history entry. Empty values and the default filter are left out of the URL.
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
