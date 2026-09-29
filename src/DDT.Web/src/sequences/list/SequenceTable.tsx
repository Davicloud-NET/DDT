// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural, t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";

import { relativeTime } from "@/lib/relativeTime";
import type { MachineSummary } from "@/machines/machines";
import type { RuleView } from "@/rules/rules";
import { Table, TableBody, TableCell, TableColumn, TableHeader, TableRow } from "@/ui/Table";

import { activeRunsOf, ruleTarget, rulesChoosing, sequenceFacts } from "../sequenceList";
import type { SequenceSummary } from "../sequences";
import { DeleteKey } from "./DeleteKey";
import { SequenceName } from "./SequenceName";
import { StateCell } from "./StateCell";
import { UsedBy } from "./UsedBy";

interface SequenceTableProps {
  shown: SequenceSummary[];
  now: number;
  ruleList: readonly RuleView[];
  machineList: readonly MachineSummary[];
  isAdministrator: boolean;
  onDelete: (id: string) => void;
}

export function SequenceTable({
  shown,
  now,
  ruleList,
  machineList,
  isAdministrator,
  onDelete,
}: SequenceTableProps) {
  const { i18n, t: translate } = useLingui();

  return (
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
      {/* A row only renders again when its sequence changes. Anything else a row shows must be listed here. */}
      <TableBody
        items={shown}
        dependencies={[now, ruleList, machineList, isAdministrator, i18n.locale]}
      >
        {(sequence) => (
          <TableRow id={sequence.id} textValue={sequence.name}>
            <TableCell className="pl-4">
              <SequenceName sequence={sequence} />
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
                    onDelete(sequence.id);
                  }}
                />
              ) : null}
            </TableCell>
          </TableRow>
        )}
      </TableBody>
    </Table>
  );
}

function stepsText(count: number): string {
  return plural(count, { one: "# step", other: "# steps" });
}

function byText(name: string): string {
  return t`by ${name}`;
}
