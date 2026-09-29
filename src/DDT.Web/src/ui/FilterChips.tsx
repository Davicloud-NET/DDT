// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { ToggleButtonGroup, type Key } from "react-aria-components";

import { FilterKey, filterWellClass, type FilterOption } from "./FilterKey";

// The same well for filters that combine, such as the levels of a log. Every button toggles on its own, and
// at least one stays on.
export function FilterChips({
  label,
  options,
  selected,
  onChange,
}: {
  label: string;
  options: FilterOption[];
  selected: ReadonlySet<string>;
  onChange: (selected: Set<string>) => void;
}) {
  return (
    <ToggleButtonGroup
      aria-label={label}
      selectionMode="multiple"
      disallowEmptySelection
      selectedKeys={selected}
      onSelectionChange={(keys: Set<Key>) => {
        onChange(new Set([...keys].map(String)));
      }}
      className={filterWellClass}
    >
      {options.map((option) => (
        <FilterKey key={option.id} option={option} />
      ))}
    </ToggleButtonGroup>
  );
}
