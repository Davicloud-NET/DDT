// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural, t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { IconPlus, IconTrash } from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { Link, useNavigate, useSearch } from "@tanstack/react-router";
import { useState } from "react";
import { Button as AriaButton } from "react-aria-components";

import { currentUserQuery } from "@/auth/auth";
import { relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import { liveListOptions } from "@/live/freshness";
import { useLiveStatus } from "@/live/useLiveStatus";
import { machinesQuery } from "@/machines/machines";
import { rulesQuery } from "@/rules/rules";
import { Button } from "@/ui/Button";
import { FilterSelector, SearchField } from "@/ui/Controls";
import { EmptyState, Page, PageHeader, Skeleton } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";
import { StateTag } from "@/ui/StateTag";
import { Table, TableBody, TableCell, TableColumn, TableHeader, TableRow } from "@/ui/Table";
import { Tooltip } from "@/ui/Tooltip";

import { DeleteSequenceDialog } from "./DeleteSequenceDialog";
import { NewSequenceDialog } from "./NewSequenceDialog";
import {
  activeRunsOf,
  findingCounts,
  inFilter,
  matchesSearch,
  ruleTarget,
  rulesChoosing,
  sequenceFacts,
  sequenceFilters,
  type SequenceFilter,
} from "./sequenceList";
import { sequencesQuery, type SequenceSummary } from "./sequences";

// Every task sequence, with what it does to a machine, whether it can run, and what uses it. The list is live: the
// hub says when a sequence, a rule or a machine changes, and it is read again on a timer only while the live
// connection is down. Administrators create and delete sequences here; everyone else looks.
export function SequencesPage() {
  const { i18n, t: translate } = useLingui();
  const search = useSearch({ from: "/shell/deployment/sequences" });
  const navigate = useNavigate({ from: "/deployment/sequences" });
  const freshness = liveListOptions(useLiveStatus());
  const sequences = useQuery({ ...sequencesQuery, ...freshness });
  const rules = useQuery({ ...rulesQuery, ...freshness });
  const machines = useQuery({ ...machinesQuery, ...freshness });
  const user = useQuery(currentUserQuery).data ?? null;
  const now = useNow(30_000);

  const [creating, setCreating] = useState(false);
  const [deletingId, setDeletingId] = useState<string | null>(null);

  const isAdministrator = user?.roles.includes("Administrator") === true;
  const filter: SequenceFilter = search.state ?? "all";
  const query = search.q ?? "";
  const all = sequences.data ?? [];
  const ruleList = rules.data ?? [];
  const machineList = machines.data ?? [];
  const matching = all.filter((sequence) => matchesSearch(sequence, query));
  const shown = matching.filter((sequence) => inFilter(sequence, filter));
  const deleting = all.find((sequence) => sequence.id === deletingId) ?? null;

  // Changes the view without a new history entry. Empty values and the default filter leave the address.
  function setSearch(next: { state?: SequenceFilter; q?: string }) {
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
  }

  return (
    <Page>
      <PageHeader title={<Trans>Task sequences</Trans>}>
        {all.length > 0 ? (
          <FilterSelector
            label={translate`Show task sequences by state`}
            selected={filter}
            onChange={(id) => {
              setSearch({ state: id as SequenceFilter });
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
        {all.length > 0 ? (
          <SearchField
            label={translate`Find a task sequence`}
            placeholder={translate`Name or description`}
            value={query}
            onChange={(q) => {
              setSearch({ q });
            }}
          />
        ) : null}
        {isAdministrator ? (
          <Button
            variant="primary"
            onPress={() => {
              setCreating(true);
            }}
          >
            <IconPlus aria-hidden="true" size={18} stroke={2} />
            <Trans>New task sequence</Trans>
          </Button>
        ) : null}
      </PageHeader>

      <p className="max-w-[75ch] text-ink-2">
        <Trans>
          A task sequence is the list of steps a machine runs, such as partitioning the disk,
          applying an image and running scripts. One with problems is kept as a draft and cannot run
          until they are fixed.
        </Trans>
      </p>

      {sequences.isError ? (
        <Notice tone="fail">
          <Trans>The task sequence list could not be loaded.</Trans>
        </Notice>
      ) : null}

      <section className="overflow-hidden rounded-panel bg-panel shadow-panel">
        {sequences.isPending ? (
          <LoadingRows />
        ) : all.length === 0 ? (
          <EmptyState
            title={<Trans>No task sequences yet</Trans>}
            action={
              isAdministrator ? (
                <Button
                  variant="primary"
                  onPress={() => {
                    setCreating(true);
                  }}
                >
                  <Trans>New task sequence</Trans>
                </Button>
              ) : null
            }
          >
            {isAdministrator ? (
              <Trans>
                Start from a template, such as Install Windows: it partitions the disk, applies an
                image, adds the drivers for the machine's model and writes the answer file. Or start
                empty and add the steps yourself.
              </Trans>
            ) : (
              <Trans>
                An administrator creates task sequences here. Until then, no machine can be given
                one.
              </Trans>
            )}
          </EmptyState>
        ) : shown.length === 0 ? (
          <EmptyState
            title={<Trans>No task sequence matches</Trans>}
            action={
              <Button
                onPress={() => {
                  setSearch({ state: "all", q: "" });
                }}
              >
                <Trans>Show all task sequences</Trans>
              </Button>
            }
          />
        ) : (
          <Table aria-label={translate`Task sequences`} className="min-w-[960px] table-fixed">
            <TableHeader>
              <TableColumn id="sequence" isRowHeader className="w-[30%] pl-4">
                <Trans>Task sequence</Trans>
              </TableColumn>
              <TableColumn id="state" className="w-44">
                <Trans>State</Trans>
              </TableColumn>
              <TableColumn id="does">
                <Trans>What it does</Trans>
              </TableColumn>
              <TableColumn id="used">
                <Trans>Used by</Trans>
              </TableColumn>
              <TableColumn id="changed" className="w-40">
                <Trans>Changed</Trans>
              </TableColumn>
              <TableColumn id="actions" className="w-14 pr-4">
                <span className="sr-only">
                  <Trans>Actions</Trans>
                </span>
              </TableColumn>
            </TableHeader>
            {/* A row renders again only when its sequence changes, unless something else it shows is listed here. */}
            <TableBody
              items={shown}
              dependencies={[now, ruleList, machineList, isAdministrator, i18n.locale]}
            >
              {(sequence) => (
                <TableRow id={sequence.id} textValue={sequence.name}>
                  <TableCell className="pl-4">
                    <span className="flex min-w-0 flex-col leading-tight">
                      <Link
                        to="/deployment/sequences/$sequenceId"
                        params={{ sequenceId: sequence.id }}
                        className="truncate type-label text-[16.5px] hover:underline"
                      >
                        {sequence.name}
                      </Link>
                      {sequence.description !== null ? (
                        <span
                          className="truncate type-small text-muted"
                          title={sequence.description}
                        >
                          {sequence.description}
                        </span>
                      ) : null}
                    </span>
                  </TableCell>
                  <TableCell>
                    <StateCell sequence={sequence} />
                  </TableCell>
                  <TableCell>
                    <span className="flex min-w-0 flex-col">
                      <span>{stepsText(sequence.stepCount)}</span>
                      <span className="line-clamp-2 type-small text-muted">
                        {sequenceFacts(sequence).join(", ")}
                      </span>
                    </span>
                  </TableCell>
                  <TableCell>
                    <UsedBy
                      targets={rulesChoosing(ruleList, sequence.id).map(ruleTarget)}
                      activeRuns={activeRunsOf(machineList, sequence.id)}
                    />
                  </TableCell>
                  <TableCell className="type-small whitespace-nowrap">
                    <span className="flex flex-col">
                      <span title={new Date(sequence.updatedUtc).toLocaleString(i18n.locale)}>
                        {relativeTime(sequence.updatedUtc, now)}
                      </span>
                      {sequence.updatedBy !== null ? (
                        <span className="truncate text-muted">{byText(sequence.updatedBy)}</span>
                      ) : null}
                    </span>
                  </TableCell>
                  <TableCell className="pr-4">
                    {isAdministrator ? (
                      <DeleteKey
                        name={sequence.name}
                        onPress={() => {
                          setDeletingId(sequence.id);
                        }}
                      />
                    ) : null}
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        )}
      </section>

      {creating ? (
        <NewSequenceDialog
          taken={all.map((sequence) => sequence.name)}
          onClose={() => {
            setCreating(false);
          }}
        />
      ) : null}

      {deleting !== null ? (
        <DeleteSequenceDialog
          sequence={deleting}
          rules={rulesChoosing(ruleList, deleting.id)}
          activeRuns={activeRunsOf(machineList, deleting.id)}
          onClose={() => {
            setDeletingId(null);
          }}
        />
      ) : null}
    </Page>
  );
}

function stepsText(count: number): string {
  return plural(count, { one: "# step", other: "# steps" });
}

function byText(name: string): string {
  return t`by ${name}`;
}

// Whether the sequence can run. A sequence with problems is a draft; warnings only tell.
function StateCell({ sequence }: { sequence: SequenceSummary }) {
  const counts = findingCounts(sequence.problemCount, sequence.warningCount);

  return (
    <span className="flex flex-col items-start gap-1">
      <StateTag tone={sequence.problemCount > 0 ? "fail" : "ok"}>
        {sequence.problemCount > 0 ? <Trans>Cannot run</Trans> : <Trans>Ready to run</Trans>}
      </StateTag>
      {counts !== null ? <span className="type-small text-muted">{counts}</span> : null}
    </span>
  );
}

// The rules that choose the sequence, and the machines it is assigned to or running on now.
function UsedBy({ targets, activeRuns }: { targets: string[]; activeRuns: number }) {
  if (targets.length === 0 && activeRuns === 0) {
    return (
      <span className="type-small text-muted">
        <Trans>No rule, no machine</Trans>
      </span>
    );
  }

  return (
    <span className="flex min-w-0 flex-col type-small">
      {targets.map((target) => (
        <span key={target} className="truncate text-ink">
          <Trans>Rule for {target}</Trans>
        </span>
      ))}
      {activeRuns > 0 ? (
        <span className="text-run-text">
          {plural(activeRuns, {
            one: "Assigned to or running on # machine",
            other: "Assigned to or running on # machines",
          })}
        </span>
      ) : null}
    </span>
  );
}

function DeleteKey({ name, onPress }: { name: string; onPress: () => void }) {
  return (
    <Tooltip content={<Trans>Delete</Trans>}>
      <AriaButton
        aria-label={t`Delete ${name}`}
        onPress={onPress}
        className="flex size-7.5 cursor-pointer items-center justify-center rounded-key text-muted key-motion outline-none hover:bg-hover pressed:bg-key-quiet-pressed hover:text-fail-text focus-visible:outline-2 focus-visible:outline-focus"
      >
        <IconTrash size={17} stroke={2} />
      </AriaButton>
    </Tooltip>
  );
}

function LoadingRows() {
  return (
    <div aria-hidden="true" className="flex flex-col">
      <div className="h-10 border-b border-line" />
      {Array.from({ length: 4 }, (_, index) => (
        <div key={index} className="flex h-14 items-center gap-6 border-b border-line-soft px-4">
          <span className="flex w-72 flex-col gap-1.5">
            <Skeleton className="h-3.5 w-44" />
            <Skeleton className="h-3 w-60" />
          </span>
          <Skeleton className="h-6 w-24" />
          <Skeleton className="h-3.5 flex-1" />
        </div>
      ))}
    </div>
  );
}
