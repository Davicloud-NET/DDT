// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useState } from "react";

import { useListSearch } from "@/ui/useListSearch";

import type { ApiTokenView } from "./tokens";
import { byState, inTokenFilter, matchesToken, type TokenFilter } from "./tokenView";

// The token list's state filter and search, with how many tokens each state would show.
export function useTokenFilter(tokens: ApiTokenView[], now: number) {
  const [filter, setFilter] = useState<TokenFilter>("active");
  const { query, setQuery, shown: matching } = useListSearch(tokens, matchesToken);

  return {
    filter,
    setFilter,
    query,
    setQuery,
    shown: byState(
      matching.filter((token) => inTokenFilter(token, filter, now)),
      now,
    ),
    count: (state: TokenFilter) =>
      matching.filter((token) => inTokenFilter(token, state, now)).length,
    showAll: () => {
      setFilter("all");
      setQuery("");
    },
  };
}
