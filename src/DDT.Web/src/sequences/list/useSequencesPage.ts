// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { useState } from "react";

import { useIsAdministrator } from "@/auth/useIsAdministrator";
import { useNow } from "@/lib/useNow";
import { liveListOptions } from "@/live/freshness";
import { useLiveStatus } from "@/live/useLiveStatus";
import { machinesQuery } from "@/machines/machines";
import { rulesQuery } from "@/rules/rules";

import { inFilter, matchesSearch, type SequenceFilter } from "../sequenceList";
import { sequencesQuery } from "../sequences";

// The sequence list's data, which the hub keeps current and a timer reads only while the live connection is down, the
// view the address holds, and the open dialog.
export function useSequencesPage() {
  const search = useSearch({ from: "/shell/deployment/sequences" });
  const navigate = useNavigate({ from: "/deployment/sequences" });
  const freshness = liveListOptions(useLiveStatus());
  const sequences = useQuery({ ...sequencesQuery, ...freshness });
  const rules = useQuery({ ...rulesQuery, ...freshness });
  const machines = useQuery({ ...machinesQuery, ...freshness });
  const now = useNow(30_000);

  const [creating, setCreating] = useState(false);
  const [deletingId, setDeletingId] = useState<string | null>(null);

  const isAdministrator = useIsAdministrator();
  const filter: SequenceFilter = search.state ?? "all";
  const query = search.q ?? "";
  const all = sequences.data ?? [];
  const ruleList = rules.data ?? [];
  const machineList = machines.data ?? [];
  const matching = all.filter((sequence) => matchesSearch(sequence, query));
  const shown = matching.filter((sequence) => inFilter(sequence, filter));
  const deleting = all.find((sequence) => sequence.id === deletingId) ?? null;

  // Changes the view without a new history entry. Empty values and the default filter leave the address.
  function setSearch(next: { state?: SequenceFilter; q?: string }) {
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

  return {
    sequences,
    now,
    creating,
    setCreating,
    setDeletingId,
    isAdministrator,
    filter,
    query,
    all,
    ruleList,
    machineList,
    matching,
    shown,
    deleting,
    setSearch,
  };
}
