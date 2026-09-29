// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";
import { Header, MenuItem, MenuSection } from "react-aria-components";

import { cx } from "@/ui/cx";

import type { PaletteEntry } from "./usePaletteEntries";

// One kind of entry in the palette's results. It's left out while it has no entries.
export function PaletteSection({ title, entries }: { title: ReactNode; entries: PaletteEntry[] }) {
  if (entries.length === 0) {
    return null;
  }

  return (
    <MenuSection className="pb-1">
      <Header className="px-2.5 pt-2 pb-1 type-small text-muted">{title}</Header>
      {entries.map((entry) => (
        <MenuItem
          key={entry.id}
          id={entry.id}
          textValue={entry.label}
          className={({ isFocused }) =>
            cx(
              "flex cursor-pointer items-baseline gap-3 rounded-key px-2.5 py-2 motion-highlight outline-none",
              isFocused && "bg-selected",
            )
          }
        >
          <span className="truncate type-label text-ink">{entry.label}</span>
          {entry.detail !== undefined ? (
            <span className="truncate type-small text-muted">{entry.detail}</span>
          ) : null}
        </MenuItem>
      ))}
    </MenuSection>
  );
}
