// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { useNow } from "@/lib/useNow";
import { Button } from "@/ui/Button";
import { EmptyState } from "@/ui/EmptyState";
import { FilterSelector } from "@/ui/FilterSelector";
import { ListSkeleton } from "@/ui/ListSkeleton";
import { Notice } from "@/ui/Notice";
import { Page } from "@/ui/Page";
import { PageHeader } from "@/ui/PageHeader";
import { Panel } from "@/ui/Panel";
import { SearchField } from "@/ui/SearchField";

import { RunHistoryTable } from "./history/RunHistoryTable";
import { useRunHistory } from "./history/useRunHistory";
import { useRunHistorySearch } from "./history/useRunHistorySearch";
import { runFilters, type RunFilter } from "./runHistory";

// Every run of every machine, newest first, a page at a time. New runs and every change of a run arrive live from the
// hub: a new run enters at the top, and a run whose state changes flashes. The server filters, searches and counts.
export function RunHistoryPage() {
  const { i18n, t: translate } = useLingui();
  const { filter, query, typed, setTyped, setSearch } = useRunHistorySearch();
  const now = useNow(30_000);
  const { history, mark, tally, items, total } = useRunHistory(filter, query);

  return (
    <Page>
      <PageHeader title={<Trans>Run history</Trans>}>
        <FilterSelector
          label={translate`Show runs by state`}
          selected={filter}
          onChange={(id) => {
            setSearch({ state: id as RunFilter });
          }}
          options={runFilters.map((option) => ({
            id: option.id,
            label: i18n._(option.label),
            ...(tally === null
              ? {}
              : { count: option.count === null ? (total ?? 0) : tally[option.count] }),
            ...(option.tone === undefined ? {} : { tone: option.tone }),
          }))}
        />
        <div className="hidden flex-1 sm:block" />
        <SearchField
          label={translate`Find a run`}
          placeholder={translate`Machine, model, MAC or sequence`}
          value={typed}
          onChange={setTyped}
        />
      </PageHeader>

      {history.isError ? (
        <Notice tone="fail">
          <Trans>The run history could not be loaded.</Trans>
        </Notice>
      ) : null}

      <Panel flush>
        {history.isPending ? (
          <ListSkeleton widths={["w-1/2", "w-2/3", "w-1/3"]} />
        ) : items.length === 0 ? (
          filter === "all" && query === "" ? (
            <EmptyState title={<Trans>No runs yet</Trans>}>
              <Trans>
                A run starts when a machine is assigned a task sequence, is approved with the one a
                rule chose, or someone chooses one at the machine.
              </Trans>
            </EmptyState>
          ) : (
            <EmptyState title={<Trans>No run matches</Trans>} />
          )
        ) : (
          <RunHistoryTable items={items} now={now} mark={mark} />
        )}
        {history.hasNextPage ? (
          <div className="border-t border-line-soft px-4 py-3">
            <Button
              isDisabled={history.isFetchingNextPage}
              onPress={() => {
                void history.fetchNextPage();
              }}
            >
              {history.isFetchingNextPage ? (
                <Trans>Loading older runs</Trans>
              ) : (
                <Trans>Load older runs</Trans>
              )}
            </Button>
          </div>
        ) : null}
      </Panel>
    </Page>
  );
}
