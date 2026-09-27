// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { useInfiniteQuery } from "@tanstack/react-query";
import { Link, useNavigate, useSearch } from "@tanstack/react-router";
import { useCallback, useEffect, useState } from "react";

import { currentStepLabel } from "@/deployments/deployments";
import { formattingLocale } from "@/i18n/i18n";
import { formatDuration } from "@/lib/format";
import { useNow } from "@/lib/useNow";
import { useLiveMarks } from "@/live/useLiveMarks";
import { formatMac } from "@/machines/machines";
import { railFromSummary, railLabel } from "@/machines/machineView";
import { Button } from "@/ui/Button";
import { FilterSelector, SearchField } from "@/ui/Controls";
import { DeviceGlyph, type DeviceKind } from "@/ui/DeviceGlyph";
import { EmptyState, Page, PageHeader, Panel, Skeleton } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";
import { SequenceRailStrip } from "@/ui/SequenceRail";
import { StateTag } from "@/ui/StateTag";
import { Table, TableBody, TableCell, TableColumn, TableHeader, TableRow } from "@/ui/Table";

import { runFilters, runHistoryQuery, type RunFilter, type RunHistoryItem } from "./runHistory";
import { runStateLabel, runStateTone } from "./runView";

const deviceKinds: Record<RunHistoryItem["deviceKind"], DeviceKind> = {
  Unknown: "unknown",
  Laptop: "laptop",
  Desktop: "desktop",
  Tablet: "tablet",
  Server: "server",
  Virtual: "virtual",
};

// Every run of every machine, newest first, a page at a time. New runs and every change of a run arrive live from the
// hub: a new run enters at the top, and a run whose state changes flashes. The filter and the search go to the
// server, which also counts the runs by state.
export function RunHistoryPage() {
  const { i18n, t: translate } = useLingui();
  const search = useSearch({ from: "/shell/machines/runs" });
  const navigate = useNavigate({ from: "/machines/runs" });
  const now = useNow(30_000);
  const filter = search.state ?? "all";
  const [typed, setTyped] = useState(search.q ?? "");
  const query = search.q ?? "";

  // Changes the view without a new history entry. Empty values and the default filter leave the address.
  const setSearch = useCallback(
    (next: { state?: RunFilter; q?: string }) => {
      void navigate({
        search: (previous) => {
          const merged: Record<string, string | undefined> = { ...previous, ...next };

          return Object.fromEntries(
            Object.entries(merged).filter(
              ([key, value]) =>
                value !== undefined && value !== "" && !(key === "state" && value === "all"),
            ),
          );
        },
        replace: true,
      });
    },
    [navigate],
  );

  // The search goes to the server, so it waits until typing pauses.
  useEffect(() => {
    if (typed === query) {
      return;
    }

    const timer = window.setTimeout(() => {
      setSearch({ q: typed });
    }, 300);

    return () => {
      window.clearTimeout(timer);
    };
  }, [typed, query, setSearch]);

  const states = runFilters.find((candidate) => candidate.id === filter)?.states ?? [];
  const history = useInfiniteQuery(runHistoryQuery({ states, query }));
  const mark = useLiveMarks({
    queryKey: runHistoryQuery({ states, query }).queryKey,
    items: (data) => data.pages.flatMap((page) => page.items),
    id: (item) => item.run.id,
    signature: (item) => item.run.state,
    tone: (item) => runStateTone[item.run.state],
  });
  // The counts cover every state, so they come from the first page of the unfiltered state.
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
  const locale = formattingLocale();

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
          <div className="flex flex-col gap-3 p-4">
            <Skeleton className="h-6 w-1/2" />
            <Skeleton className="h-6 w-2/3" />
            <Skeleton className="h-6 w-1/3" />
          </div>
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
          <Table aria-label={translate`Runs`} className="min-w-[900px] table-fixed">
            <TableHeader>
              <TableColumn id="machine" isRowHeader className="w-[28%] pl-4">
                <Trans>Machine</Trans>
              </TableColumn>
              <TableColumn id="run">
                <Trans>Sequence</Trans>
              </TableColumn>
              <TableColumn id="state" className="w-40">
                <Trans>State</Trans>
              </TableColumn>
              <TableColumn id="started" className="w-48 pr-4">
                <Trans>Started</Trans>
              </TableColumn>
            </TableHeader>
            <TableBody items={items} dependencies={[now, i18n.locale, mark]}>
              {(item) => {
                const run = item.run;
                const failedAt = run.state === "Failed" ? currentStepLabel(run) : null;
                const rail = railFromSummary(run);

                return (
                  <TableRow id={run.id} textValue={run.title} className={mark(run.id)}>
                    <TableCell className="pl-4">
                      <span className="flex min-w-0 items-center gap-3">
                        <DeviceGlyph kind={deviceKinds[item.deviceKind]} />
                        <span className="flex min-w-0 flex-col leading-tight">
                          <Link
                            to="/machines/$machineId"
                            params={{ machineId: item.machineId }}
                            search={{ run: run.id }}
                            className="truncate type-label hover:underline"
                          >
                            {machineName(item)}
                          </Link>
                          <span className="truncate type-small text-muted">
                            {machineLine(item)}
                          </span>
                        </span>
                      </span>
                    </TableCell>
                    <TableCell>
                      <span className="flex min-w-0 flex-col gap-1.5 pr-4">
                        <span className="truncate">{run.title}</span>
                        {rail.length > 0 ? (
                          <SequenceRailStrip steps={rail} label={railLabel(run)} />
                        ) : null}
                      </span>
                    </TableCell>
                    <TableCell>
                      <span className="flex flex-col items-start gap-1">
                        <StateTag tone={runStateTone[run.state]}>
                          {i18n._(runStateLabel[run.state])}
                        </StateTag>
                        {failedAt !== null ? (
                          <span className="truncate type-small text-fail-text">
                            <Trans>At {failedAt}</Trans>
                          </span>
                        ) : null}
                      </span>
                    </TableCell>
                    <TableCell className="pr-4 type-small">
                      <span className="flex flex-col">
                        <span>
                          {new Date(run.startedUtc ?? run.createdUtc).toLocaleString(locale)}
                        </span>
                        <span className="text-muted">{durationLine(run, now)}</span>
                      </span>
                    </TableCell>
                  </TableRow>
                );
              }}
            </TableBody>
          </Table>
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

function machineName(item: RunHistoryItem): string {
  return item.machineName ?? item.machineModel ?? formatMac(item.primaryMac);
}

// What the name leaves out: the model under a computer name, else the maker and the MAC address.
function machineLine(item: RunHistoryItem): string {
  const mac = formatMac(item.primaryMac);

  if (item.machineName !== null) {
    return item.machineModel ?? mac;
  }

  return item.manufacturer === null ? mac : `${item.manufacturer}, ${mac}`;
}

function durationLine(run: RunHistoryItem["run"], now: number): string {
  if (run.startedUtc === null) {
    return t`Not started`;
  }

  const end = run.finishedUtc === null ? now : Date.parse(run.finishedUtc);
  const took = formatDuration(end - Date.parse(run.startedUtc));

  return run.finishedUtc === null ? t`Running for ${took}` : t`Took ${took}`;
}
