// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { relativeTime } from "@/lib/relativeTime";
import { MachineActions } from "@/machines/MachineActions";
import { deviceKind, displayName } from "@/machines/machineView";
import { DeviceGlyph } from "@/ui/DeviceGlyph";
import { Table, TableBody, TableCell, TableColumn, TableHeader, TableRow } from "@/ui/Table";

import { MachineName } from "./MachineName";
import type { MachineRowsProps } from "./machineRows";
import { MachineStateTag } from "./MachineStateTag";
import { RunCell } from "./RunCell";

interface MachineTableProps extends MachineRowsProps {
  selectedId: string | null;
  onSelect: (id: string | undefined) => void;
}

// The machine list as a table, where picking a row opens its details beside the list.
export function MachineTable({
  machines,
  now,
  canDecide,
  actions,
  strays,
  mark,
  selectedId,
  onSelect,
}: MachineTableProps) {
  const { i18n, t: translate } = useLingui();

  return (
    <Table
      aria-label={translate`Machines`}
      selectionMode="single"
      selectionBehavior="replace"
      className="min-w-[880px] table-fixed"
      selectedKeys={selectedId === null ? [] : [selectedId]}
      onSelectionChange={(keys) => {
        const [key] = keys === "all" ? [] : [...keys];
        onSelect(key === undefined ? undefined : String(key));
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
        items={machines}
        dependencies={[now, canDecide, actions, strays, i18n.locale, mark]}
      >
        {(machine) => (
          <TableRow id={machine.id} textValue={displayName(machine)} className={mark(machine.id)}>
            <TableCell className="pl-4">
              <span className="flex min-w-0 items-center gap-3">
                <DeviceGlyph kind={deviceKind(machine)} />
                <MachineName machine={machine} className="flex min-w-0 flex-col leading-tight" />
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
  );
}
