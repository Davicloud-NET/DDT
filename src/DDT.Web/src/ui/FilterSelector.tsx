// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { ToggleButtonGroup, type Key } from "react-aria-components";

import { FilterKey, filterWellClass, type FilterOption } from "./FilterKey";

// A selector for filtering a list by state: a well with one button per state, the chosen one raised out of it.
// The buttons differ in width, so the raised one doesn't slide over. One sinks and the other rises, in colour.
export function FilterSelector({
  label,
  options,
  selected,
  onChange,
}: {
  label: string;
  options: FilterOption[];
  selected: string;
  onChange: (id: string) => void;
}) {
  return (
    <ToggleButtonGroup
      aria-label={label}
      selectionMode="single"
      disallowEmptySelection
      selectedKeys={[selected]}
      onSelectionChange={(keys: Set<Key>) => {
        const [key] = keys;

        if (key !== undefined) {
          onChange(String(key));
        }
      }}
      className={filterWellClass}
    >
      {options.map((option) => (
        <FilterKey key={option.id} option={option} />
      ))}
    </ToggleButtonGroup>
  );
}
