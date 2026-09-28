// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import type { MachineSummary } from "@/machines/machines";
import { inFilter, machineFilters, type MachineFilter } from "@/machines/machineView";
import { FilterSelector } from "@/ui/FilterSelector";
import { PageHeader } from "@/ui/PageHeader";
import { SearchField } from "@/ui/SearchField";

import type { MachinesSearchChange } from "./useMachineFilters";

interface MachineListHeaderProps {
  filter: MachineFilter;
  query: string;
  // The machines the search leaves, which each filter counts.
  matching: readonly MachineSummary[];
  onChange: (next: MachinesSearchChange) => void;
}

// The list's title with the filters by state and the search.
export function MachineListHeader({ filter, query, matching, onChange }: MachineListHeaderProps) {
  const { i18n, t: translate } = useLingui();

  return (
    <PageHeader title={<Trans>All machines</Trans>}>
      <FilterSelector
        label={translate`Show machines by state`}
        selected={filter}
        onChange={(id) => {
          onChange({ state: id as MachineFilter });
        }}
        options={machineFilters.map((option) => ({
          id: option.id,
          label: i18n._(option.label),
          count: matching.filter((machine) => inFilter(machine, option.id)).length,
          ...(option.tone === undefined ? {} : { tone: option.tone }),
        }))}
      />
      <div className="hidden flex-1 sm:block" />
      <SearchField
        label={translate`Find a machine`}
        placeholder={translate`Name, MAC, serial or address`}
        value={query}
        onChange={(q) => {
          onChange({ q });
        }}
      />
    </PageHeader>
  );
}
