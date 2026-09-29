// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconSearch } from "@tabler/icons-react";
import { useNavigate } from "@tanstack/react-router";
import { Autocomplete, Input, Menu, SearchField, useFilter } from "react-aria-components";

import { PaletteSection } from "./PaletteSection";
import { usePaletteEntries } from "./usePaletteEntries";

// The palette's search and its results. Choosing a result goes there and closes the palette.
export function Palette({ onDone }: { onDone: () => void }) {
  const { t } = useLingui();
  const navigate = useNavigate();
  const { contains } = useFilter({ sensitivity: "base" });
  const { pages, machineEntries, sequenceEntries, imageEntries, all } = usePaletteEntries();

  return (
    <Autocomplete
      filter={(text, input, node) => {
        const entry = all.find((candidate) => candidate.id === node.key);

        return (
          contains(text, input) ||
          (entry?.keywords !== undefined &&
            contains(entry.keywords.replace(/[-:]/g, ""), input.replace(/[-:]/g, "")))
        );
      }}
    >
      <SearchField
        aria-label={t`Search`}
        autoFocus
        className="flex items-center gap-2.5 border-b border-line-soft px-4"
      >
        <IconSearch aria-hidden="true" size={18} stroke={2} className="text-muted" />
        <Input
          placeholder={t`Go to a page, machine, sequence or image`}
          className="h-13 flex-1 bg-transparent type-body text-ink outline-none placeholder:text-placeholder [&::-webkit-search-cancel-button]:hidden"
        />
      </SearchField>
      <Menu
        aria-label={t`Results`}
        onAction={(key) => {
          const entry = all.find((candidate) => candidate.id === key);

          if (entry !== undefined) {
            onDone();
            void navigate(
              entry.params === undefined
                ? { to: entry.to }
                : { to: entry.to, params: entry.params },
            );
          }
        }}
        renderEmptyState={() => (
          <p className="px-4 py-6 type-small text-muted">
            <Trans>Nothing matches.</Trans>
          </p>
        )}
        className="max-h-[55vh] overflow-y-auto p-1.5 outline-none"
      >
        <PaletteSection title={<Trans>Pages</Trans>} entries={pages} />
        <PaletteSection title={<Trans>Machines</Trans>} entries={machineEntries} />
        <PaletteSection title={<Trans>Task sequences</Trans>} entries={sequenceEntries} />
        <PaletteSection title={<Trans>OS images</Trans>} entries={imageEntries} />
      </Menu>
    </Autocomplete>
  );
}
