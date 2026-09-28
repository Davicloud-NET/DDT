// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { Link } from "@tanstack/react-router";

import { currentStepLabel } from "@/deployments/deployments";
import { formattingLocale } from "@/i18n/i18n";
import { railFromSummary, railLabel } from "@/machines/machineView";
import { DeviceGlyph, type DeviceKind } from "@/ui/DeviceGlyph";
import { SequenceRailStrip } from "@/ui/SequenceRailStrip";
import { StateTag } from "@/ui/StateTag";
import { Table, TableBody, TableCell, TableColumn, TableHeader, TableRow } from "@/ui/Table";

import type { RunHistoryItem } from "../runHistory";
import { runStateLabel, runStateTone } from "../runView";
import { durationLine, machineLine, machineName } from "./runHistoryText";

const deviceKinds: Record<RunHistoryItem["deviceKind"], DeviceKind> = {
  Unknown: "unknown",
  Laptop: "laptop",
  Desktop: "desktop",
  Tablet: "tablet",
  Server: "server",
  Virtual: "virtual",
};

interface RunHistoryTableProps {
  items: RunHistoryItem[];
  now: number;
  // The live mark's classes of a run's row.
  mark: (id: string) => string;
}

export function RunHistoryTable({ items, now, mark }: RunHistoryTableProps) {
  const { i18n, t: translate } = useLingui();
  const locale = formattingLocale();

  return (
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
                <RunMachine item={item} />
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
                  <span>{new Date(run.startedUtc ?? run.createdUtc).toLocaleString(locale)}</span>
                  <span className="text-muted">{durationLine(run, now)}</span>
                </span>
              </TableCell>
            </TableRow>
          );
        }}
      </TableBody>
    </Table>
  );
}

// The machine a run ran on, with a way to the run on the machine's page.
function RunMachine({ item }: { item: RunHistoryItem }) {
  return (
    <span className="flex min-w-0 items-center gap-3">
      <DeviceGlyph kind={deviceKinds[item.deviceKind]} />
      <span className="flex min-w-0 flex-col leading-tight">
        <Link
          to="/machines/$machineId"
          params={{ machineId: item.machineId }}
          search={{ run: item.run.id }}
          className="truncate type-label hover:underline"
        >
          {machineName(item)}
        </Link>
        <span className="truncate type-small text-muted">{machineLine(item)}</span>
      </span>
    </span>
  );
}
