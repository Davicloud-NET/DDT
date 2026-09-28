// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";
import { Link, useNavigate, useSearch } from "@tanstack/react-router";

import { currentUserQuery } from "@/auth/auth";
import {
  activityLabel,
  assignedBy,
  isActive,
  isSilentActivity,
  isWaiting,
} from "@/deployments/deployments";
import { formatDuration } from "@/lib/format";
import { relativeTime } from "@/lib/relativeTime";
import { useMediaQuery } from "@/lib/useMediaQuery";
import { useNow } from "@/lib/useNow";
import { liveListOptions } from "@/live/freshness";
import { useLiveMarks } from "@/live/useLiveMarks";
import { useLiveStatus } from "@/live/useLiveStatus";
import { isStray, machinesQuery, type MachineSummary } from "@/machines/machines";
import { useMachineActions } from "@/machines/useMachineActions";
import { Button } from "@/ui/Button";
import { FilterSelector, SearchField } from "@/ui/Controls";
import { cx } from "@/ui/cx";
import { DeviceGlyph } from "@/ui/DeviceGlyph";
import { EmptyState, Page, PageHeader, Skeleton } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";
import { SequenceRailStrip } from "@/ui/SequenceRail";
import { StateTag } from "@/ui/StateTag";
import { Table, TableBody, TableCell, TableColumn, TableHeader, TableRow } from "@/ui/Table";

import { MachineActionErrors, MachineActions } from "./MachineActions";
import { MachinePanel } from "./MachinePanel";
import {
  byAttention,
  deviceKind,
  displayName,
  hardwareLine,
  inFilter,
  machineFilters,
  machineTag,
  matchesSearch,
  railFromSummary,
  railLabel,
  type MachineFilter,
} from "./machineView";

// Every machine that netbooted. The list is live: the hub pushes each change of a machine and its run, and the
// page patches the row in place. A machine whose state changed flashes in its new state's colour, and one that
// appears enters. It reads the list again on a timer only while the live connection is down.
export function MachinesPage() {
  const { i18n, t: translate } = useLingui();
  const search = useSearch({ from: "/shell/machines" });
  const navigate = useNavigate({ from: "/machines" });
  const live = useLiveStatus();
  const machines = useQuery({ ...machinesQuery, ...liveListOptions(live) });
  const user = useQuery(currentUserQuery).data ?? null;
  const now = useNow(5_000);
  const narrow = useMediaQuery("(max-width: 767px)");
  const actions = useMachineActions();
  const mark = useLiveMarks({
    queryKey: machinesQuery.queryKey,
    items: (list) => list,
    id: (machine) => machine.id,
    // A run that comes to wait for someone flashes in the colour for that.
    signature: (machine) => `${machine.state} ${String(isWaiting(machine.deployment))}`,
    tone: (machine) => machineTag(machine).tone,
  });

  const roles = user?.roles ?? [];
  const canDecide = roles.includes("Administrator") || roles.includes("Operator");
  const filter: MachineFilter = search.state ?? "all";
  const query = search.q ?? "";
  const all = machines.data ?? [];
  const matching = all.filter((machine) => matchesSearch(machine, query));
  const shown = matching.filter((machine) => inFilter(machine, filter)).sort(byAttention);
  const selected = all.find((machine) => machine.id === search.selected) ?? null;
  const strays = straysOffer(all);

  // Changes the view without a new history entry. Empty values and the default filter leave the address.
  function setSearch(next: { state?: MachineFilter; q?: string; selected?: string | undefined }) {
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
      <PageHeader title={<Trans>All machines</Trans>}>
        <FilterSelector
          label={translate`Show machines by state`}
          selected={filter}
          onChange={(id) => {
            setSearch({ state: id as MachineFilter });
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
            setSearch({ q });
          }}
        />
      </PageHeader>

      {machines.isError ? (
        <Notice tone="fail">
          <Trans>The machine list could not be loaded.</Trans>
        </Notice>
      ) : null}

      <div className="flex items-start gap-4">
        <section className="min-w-0 flex-1 overflow-hidden rounded-panel bg-panel shadow-panel">
          {machines.isPending ? (
            <LoadingRows />
          ) : all.length === 0 ? (
            <EmptyState title={<Trans>No machines yet</Trans>}>
              <Trans>
                Machines appear here on their own when they netboot on a network DDT answers, within
                a few seconds of starting. Nothing is erased until someone signs in at the machine
                or assigns it a sequence here.
              </Trans>
            </EmptyState>
          ) : shown.length === 0 ? (
            <EmptyState
              title={<Trans>No machine matches</Trans>}
              action={
                <Button
                  onPress={() => {
                    setSearch({ state: "all", q: "" });
                  }}
                >
                  <Trans>Show all machines</Trans>
                </Button>
              }
            />
          ) : narrow ? (
            // A phone shows each machine as a row of its own that opens the machine's page; a table would scroll
            // sideways and hide its state.
            <ul aria-label={translate`Machines`} className="flex flex-col">
              {shown.map((machine) => (
                <li
                  key={machine.id}
                  className={cx(
                    "flex flex-col gap-2 border-b border-line-soft px-4 py-3 last:border-b-0",
                    mark(machine.id),
                  )}
                >
                  <span className="flex items-start gap-3">
                    <DeviceGlyph kind={deviceKind(machine)} />
                    <span className="flex min-w-0 flex-1 flex-col leading-tight">
                      <Link
                        to="/machines/$machineId"
                        params={{ machineId: machine.id }}
                        className="truncate type-label text-[16.5px] hover:underline"
                      >
                        {displayName(machine)}
                      </Link>
                      <span className="truncate type-small text-muted">
                        {hardwareLine(machine)}
                      </span>
                    </span>
                    <MachineStateTag machine={machine} />
                  </span>
                  <RunCell machine={machine} now={now} />
                  {canDecide ? (
                    <MachineActions
                      machine={machine}
                      actions={actions}
                      strays={strays.get(machine.id) ?? null}
                    />
                  ) : null}
                </li>
              ))}
            </ul>
          ) : (
            <Table
              aria-label={translate`Machines`}
              selectionMode="single"
              selectionBehavior="replace"
              className="min-w-[880px] table-fixed"
              selectedKeys={selected === null ? [] : [selected.id]}
              onSelectionChange={(keys) => {
                const [key] = keys === "all" ? [] : [...keys];
                setSearch({ selected: key === undefined ? undefined : String(key) });
              }}
            >
              <TableHeader>
                <TableColumn id="machine" isRowHeader className="w-[32%] pl-4">
                  <Trans>Machine</Trans>
                </TableColumn>
                <TableColumn id="state" className="w-36">
                  <Trans>State</Trans>
                </TableColumn>
                <TableColumn id="run">
                  <Trans>Sequence</Trans>
                </TableColumn>
                <TableColumn id="seen" className="w-36">
                  <Trans>Seen</Trans>
                </TableColumn>
                <TableColumn id="actions" className="w-40 pr-4">
                  <span className="sr-only">
                    <Trans>Actions</Trans>
                  </span>
                </TableColumn>
              </TableHeader>
              {/* A row renders again only when its machine changes, unless something else it shows is listed here. */}
              <TableBody
                items={shown}
                dependencies={[now, canDecide, actions, strays, i18n.locale, mark]}
              >
                {(machine) => (
                  <TableRow
                    id={machine.id}
                    textValue={displayName(machine)}
                    className={mark(machine.id)}
                  >
                    <TableCell className="pl-4">
                      <span className="flex min-w-0 items-center gap-3">
                        <DeviceGlyph kind={deviceKind(machine)} />
                        <span className="flex min-w-0 flex-col leading-tight">
                          <Link
                            to="/machines/$machineId"
                            params={{ machineId: machine.id }}
                            className="truncate type-label text-[16.5px] hover:underline"
                          >
                            {displayName(machine)}
                          </Link>
                          <span className="truncate type-small text-muted">
                            {hardwareLine(machine)}
                          </span>
                        </span>
                      </span>
                    </TableCell>
                    <TableCell>
                      <MachineStateTag machine={machine} />
                    </TableCell>
                    <TableCell>
                      <RunCell machine={machine} now={now} />
                    </TableCell>
                    <TableCell className="type-small whitespace-nowrap text-muted">
                      <span title={new Date(machine.lastSeenUtc).toLocaleString(i18n.locale)}>
                        {relativeTime(machine.lastSeenUtc, now)}
                      </span>
                    </TableCell>
                    <TableCell className="pr-4">
                      {canDecide ? (
                        <MachineActions
                          machine={machine}
                          actions={actions}
                          strays={strays.get(machine.id) ?? null}
                        />
                      ) : null}
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          )}
        </section>

        {selected !== null ? (
          <MachinePanel
            machine={selected}
            actions={actions}
            canDecide={canDecide}
            now={now}
            onClose={() => {
              setSearch({ selected: undefined });
            }}
          />
        ) : null}
      </div>

      <MachineActionErrors actions={actions} />
    </Page>
  );
}

// The machine's state, or that its run waits for someone.
function MachineStateTag({ machine }: { machine: MachineSummary }) {
  const { i18n } = useLingui();
  const tag = machineTag(machine);

  return <StateTag tone={tag.tone}>{i18n._(tag.label)}</StateTag>;
}

// What a row says about the machine's run, or why it has none.
function RunCell({ machine, now }: { machine: MachineSummary; now: number }) {
  const run = machine.deployment;

  if (machine.state === "Pending" && !isActive(run)) {
    const by = machine.signedInBy;
    const registered = relativeTime(machine.firstSeenUtc, now);

    return (
      <RunLines
        title={by === null ? t`Nobody has signed in yet` : t`${by} signed in at the machine`}
        detail={by === null ? t`Registered ${registered}` : t`Needs your approval`}
        detailTone={by === null ? "muted" : "ink"}
      />
    );
  }

  if (run === null) {
    return <span className="type-small text-muted">{t`No sequence assigned`}</span>;
  }

  const rail = railFromSummary(run);

  return (
    <span className="flex min-w-0 flex-col gap-1.5 pr-6">
      <RunLines
        title={run.title}
        detail={runDetail(machine, now)}
        detailTone={isWaiting(run) ? "attention" : detailTone(run.state)}
      />
      {rail.length > 0 ? <SequenceRailStrip steps={rail} label={railLabel(run)} /> : null}
    </span>
  );
}

function RunLines({
  title,
  detail,
  detailTone,
}: {
  title: string;
  detail: string | null;
  detailTone: "muted" | "ink" | "run" | "fail" | "attention";
}) {
  // Title and detail share a line where the column is wide; beside the details panel the detail goes under it.
  return (
    <span className="@container flex min-w-0">
      <span className="flex min-w-0 flex-1 flex-col @sm:flex-row @sm:items-baseline @sm:gap-3.5">
        <span className="truncate">{title}</span>
        <span className="hidden flex-1 @sm:block" />
        {detail !== null ? (
          <span
            className={cx(
              "truncate type-small @sm:shrink-0 @sm:overflow-visible",
              detailTone === "muted" && "text-muted",
              detailTone === "ink" && "text-ink",
              detailTone === "run" && "font-semibold text-run-text",
              detailTone === "fail" && "font-semibold text-fail-text",
              detailTone === "attention" && "font-semibold text-attention-text",
            )}
          >
            {detail}
          </span>
        ) : null}
      </span>
    </span>
  );
}

function detailTone(
  state: NonNullable<MachineSummary["deployment"]>["state"],
): "muted" | "run" | "fail" {
  return state === "Running" ? "run" : state === "Failed" ? "fail" : "muted";
}

function runDetail(machine: MachineSummary, now: number): string | null {
  const run = machine.deployment;

  if (run === null) {
    return null;
  }

  switch (run.state) {
    case "Assigned":
      return assignedBy(run);

    case "Running": {
      const activity = activityLabel(run.activity);

      if (activity !== null) {
        const contact = relativeTime(machine.lastSeenUtc, now);

        return isSilentActivity(run.activity) ? t`${activity}, last contact ${contact}` : activity;
      }

      const step = run.stepName;
      const percent = run.percent;

      return step === null ? t`Starting` : t`${step} ${percent}%`;
    }

    case "Failed": {
      // A run whose check before the first step failed has no step to name.
      if (run.stepIndex === null) {
        return t`Failed`;
      }

      const number = run.stepIndex + 1;

      return t`Step ${number} failed`;
    }

    case "Done": {
      if (run.startedUtc === null || run.finishedUtc === null) {
        return t`Done`;
      }

      const took = formatDuration(Date.parse(run.finishedUtc) - Date.parse(run.startedUtc));
      const finished = relativeTime(run.finishedUtc, now);

      return t`Took ${took}, finished ${finished}`;
    }

    case "Cancelled": {
      const when = run.finishedUtc === null ? null : relativeTime(run.finishedUtc, now);

      return when === null ? t`Stopped` : t`Stopped ${when}`;
    }
  }
}

// A machine that registered and waits without anyone having approved it may be a stray. Where more than one came
// from the same address, the first of them offers to remove them all.
function straysOffer(
  machines: readonly MachineSummary[],
): Map<string, { address: string; count: number }> {
  const byAddress = new Map<string, MachineSummary[]>();

  for (const machine of machines) {
    if (isStray(machine) && machine.firstSeenAddress !== null) {
      byAddress.set(machine.firstSeenAddress, [
        ...(byAddress.get(machine.firstSeenAddress) ?? []),
        machine,
      ]);
    }
  }

  const offers = new Map<string, { address: string; count: number }>();

  for (const [address, group] of byAddress) {
    const first = group[0];

    if (group.length > 1 && first !== undefined) {
      offers.set(first.id, { address, count: group.length });
    }
  }

  return offers;
}

function LoadingRows() {
  return (
    <div aria-hidden="true" className="flex flex-col">
      <div className="h-10 border-b border-line" />
      {Array.from({ length: 6 }, (_, index) => (
        <div key={index} className="flex h-14 items-center gap-3 border-b border-line-soft px-4">
          <Skeleton className="size-9.5" />
          <span className="flex w-60 flex-col gap-1.5">
            <Skeleton className="h-3.5 w-36" />
            <Skeleton className="h-3 w-48" />
          </span>
          <Skeleton className="h-6 w-24" />
          <Skeleton className="h-3.5 flex-1" />
        </div>
      ))}
    </div>
  );
}

// A refused action says why, where the page shows it; the machine's row already shows what the server stored,
// because the hub pushed it.
