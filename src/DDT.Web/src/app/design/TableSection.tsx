// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { DeviceGlyph } from "@/ui/DeviceGlyph";
import { Panel } from "@/ui/Panel";
import { StateTag } from "@/ui/StateTag";
import { Table, TableBody, TableCell, TableColumn, TableHeader, TableRow } from "@/ui/Table";

export function TableSection() {
  return (
    <Panel title="Table" flush>
      <Table
        aria-label="Machines"
        selectionMode="multiple"
        sortDescriptor={{ column: "name", direction: "ascending" }}
      >
        <TableHeader>
          <TableColumn id="name" isRowHeader allowsSorting>
            Machine
          </TableColumn>
          <TableColumn id="state">State</TableColumn>
          <TableColumn id="seen" allowsSorting>
            Seen
          </TableColumn>
        </TableHeader>
        <TableBody>
          <TableRow id="a">
            <TableCell>
              <span className="flex items-center gap-3">
                <DeviceGlyph kind="laptop" />
                LAB-PC-014
              </span>
            </TableCell>
            <TableCell>
              <StateTag tone="run">Deploying</StateTag>
            </TableCell>
            <TableCell className="text-muted">Now</TableCell>
          </TableRow>
          <TableRow id="b">
            <TableCell>
              <span className="flex items-center gap-3">
                <DeviceGlyph kind="virtual" />
                BUILD-VM-02
              </span>
            </TableCell>
            <TableCell>
              <StateTag tone="fail">Failed</StateTag>
            </TableCell>
            <TableCell className="text-muted">4 min</TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Panel>
  );
}
