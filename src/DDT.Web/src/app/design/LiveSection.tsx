// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Button } from "@/ui/Button";
import { Panel } from "@/ui/Panel";
import { SequenceRailStrip } from "@/ui/SequenceRailStrip";
import { StateTag } from "@/ui/StateTag";
import { Table, TableBody, TableCell, TableColumn, TableHeader, TableRow } from "@/ui/Table";

import { demoRail, demoTones } from "./liveDemo";
import { useLiveDemo } from "./useLiveDemo";

export function LiveSection() {
  const { machines, mark, goOn, fail, appear, startWaiting } = useLiveDemo();

  return (
    <Panel
      title="Live changes"
      flush
      actions={
        <div className="flex flex-wrap gap-2">
          <Button size="sm" onPress={goOn}>
            Go on
          </Button>
          <Button size="sm" onPress={fail}>
            Fail
          </Button>
          <Button size="sm" onPress={appear}>
            A machine appears
          </Button>
          <Button size="sm" variant="quiet" onPress={startWaiting}>
            Start the waiting
          </Button>
        </div>
      }
    >
      <Table aria-label="Machines, patched live" className="table-fixed">
        <TableHeader>
          <TableColumn id="name" isRowHeader className="w-48 pl-4">
            Machine
          </TableColumn>
          <TableColumn id="state" className="w-36">
            State
          </TableColumn>
          <TableColumn id="run" className="pr-4">
            Sequence
          </TableColumn>
        </TableHeader>
        <TableBody items={machines} dependencies={[mark]}>
          {(machine) => (
            <TableRow id={machine.id} className={mark(machine.id)}>
              <TableCell className="pl-4">{machine.name}</TableCell>
              <TableCell>
                <StateTag tone={demoTones[machine.state]}>{machine.state}</StateTag>
              </TableCell>
              <TableCell className="pr-4">
                <SequenceRailStrip steps={demoRail(machine)} label={machine.state} />
              </TableCell>
            </TableRow>
          )}
        </TableBody>
      </Table>
    </Panel>
  );
}
