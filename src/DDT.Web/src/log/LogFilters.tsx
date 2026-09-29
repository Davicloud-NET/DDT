// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { useLingui } from "@lingui/react/macro";

import { FilterChips } from "@/ui/FilterChips";
import { SearchField } from "@/ui/SearchField";

import { logLevels } from "./log";
import { levelLabel } from "./logLabels";
import type { LogView } from "./useLogView";

// The level chips, each with its count of loaded lines, and the search over the loaded lines.
export function LogFilters({ view }: { view: LogView }) {
  const { t: translate } = useLingui();
  const loaded = view.all.length;

  return (
    <div className="flex flex-wrap items-center gap-3">
      <FilterChips
        label={translate`Levels`}
        selected={view.levels}
        onChange={view.setLevels}
        options={logLevels.map((level) => ({
          id: level,
          label: levelLabel(level),
          count: view.counts.get(level) ?? 0,
          ...(level === "Error" ? { tone: "fail" as const } : {}),
          ...(level === "Warning" ? { tone: "attention" as const } : {}),
        }))}
      />
      <div className="hidden flex-1 sm:block" />
      <SearchField
        label={plural(loaded, {
          one: "Search the # loaded line",
          other: "Search the # loaded lines",
        })}
        placeholder={translate`Search the log`}
        value={view.search}
        onChange={view.setSearch}
      />
    </div>
  );
}
