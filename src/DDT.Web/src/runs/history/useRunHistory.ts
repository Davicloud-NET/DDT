// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useInfiniteQuery } from "@tanstack/react-query";

import { useLiveMarks } from "@/live/useLiveMarks";

import { runFilters, runHistoryQuery, type RunFilter } from "../runHistory";
import { runStateTone } from "../runView";

// The pages of runs read so far for a filter and a search, the live marks of their rows, and the count of each state.
export function useRunHistory(filter: RunFilter, query: string) {
  const states = runFilters.find((candidate) => candidate.id === filter)?.states ?? [];
  const history = useInfiniteQuery(runHistoryQuery({ states, query }));
  const mark = useLiveMarks({
    queryKey: runHistoryQuery({ states, query }).queryKey,
    items: (data) => data.pages.flatMap((page) => page.items),
    id: (item) => item.run.id,
    signature: (item) => item.run.state,
    tone: (item) => runStateTone[item.run.state],
  });
  // The counts cover every state, so they come from the first page of the unfiltered history.
  const counts = useInfiniteQuery({
    ...runHistoryQuery({ states: [], query }),
    enabled: filter !== "all",
  });
  const tally = (filter === "all" ? history.data : counts.data)?.pages[0]?.counts ?? null;
  const items = history.data?.pages.flatMap((page) => page.items) ?? [];
  const total =
    tally === null
      ? null
      : tally.assigned + tally.running + tally.done + tally.failed + tally.cancelled;

  return { history, mark, tally, items, total };
}
