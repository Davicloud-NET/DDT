// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconPlus } from "@tabler/icons-react";

import { Button } from "@/ui/Button";
import { FilterSelector } from "@/ui/FilterSelector";
import { PageHeader } from "@/ui/PageHeader";
import { SearchField } from "@/ui/SearchField";

import { inFilter, sequenceFilters, type SequenceFilter } from "../sequenceList";
import type { SequenceSummary } from "../sequences";

interface SequencesHeaderProps {
  // Whether there is any sequence; an empty list offers no filter and no search.
  hasSequences: boolean;
  // The sequences the search leaves, which the filter counts.
  matching: readonly SequenceSummary[];
  filter: SequenceFilter;
  query: string;
  isAdministrator: boolean;
  onSearch: (next: { state?: SequenceFilter; q?: string }) => void;
  onCreate: () => void;
}

export function SequencesHeader({
  hasSequences,
  matching,
  filter,
  query,
  isAdministrator,
  onSearch,
  onCreate,
}: SequencesHeaderProps) {
  const { i18n, t: translate } = useLingui();

  return (
    <PageHeader title={<Trans>Task sequences</Trans>}>
      {hasSequences ? (
        <FilterSelector
          label={translate`Show task sequences by state`}
          selected={filter}
          onChange={(id) => {
            onSearch({ state: id as SequenceFilter });
          }}
          options={sequenceFilters.map((option) => ({
            id: option.id,
            label: i18n._(option.label),
            count: matching.filter((sequence) => inFilter(sequence, option.id)).length,
            ...(option.tone === undefined ? {} : { tone: option.tone }),
          }))}
        />
      ) : null}
      <div className="flex-1" />
      {hasSequences ? (
        <SearchField
          label={translate`Find a task sequence`}
          placeholder={translate`Name or description`}
          value={query}
          onChange={(q) => {
            onSearch({ q });
          }}
        />
      ) : null}
      {isAdministrator ? (
        <Button variant="primary" onPress={onCreate}>
          <IconPlus aria-hidden="true" size={18} stroke={2} />
          <Trans>New task sequence</Trans>
        </Button>
      ) : null}
    </PageHeader>
  );
}
