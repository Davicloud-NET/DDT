// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useNavigate, useSearch } from "@tanstack/react-router";
import { useCallback, useEffect, useState } from "react";

import type { RunFilter } from "../runHistory";

// The run history's filter and search, kept in the address; typed is the search field's text until typing pauses.
export function useRunHistorySearch() {
  const search = useSearch({ from: "/shell/machines/runs" });
  const navigate = useNavigate({ from: "/machines/runs" });
  const filter = search.state ?? "all";
  const [typed, setTyped] = useState(search.q ?? "");
  const query = search.q ?? "";

  // Changes the view without a new history entry. Empty values and the default filter leave the address.
  const setSearch = useCallback(
    (next: { state?: RunFilter; q?: string }) => {
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
    },
    [navigate],
  );

  // The search goes to the server, so it waits until typing pauses.
  useEffect(() => {
    if (typed === query) {
      return;
    }

    const timer = window.setTimeout(() => {
      setSearch({ q: typed });
    }, 300);

    return () => {
      window.clearTimeout(timer);
    };
  }, [typed, query, setSearch]);

  return { filter, query, typed, setTyped, setSearch };
}
