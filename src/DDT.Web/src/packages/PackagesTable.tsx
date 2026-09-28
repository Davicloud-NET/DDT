// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { formatBytes } from "@/lib/format";
import { Uploaded } from "@/library/Uploaded";
import { Table, TableBody, TableCell, TableColumn, TableHeader, TableRow } from "@/ui/Table";

import { DriverTargets } from "./DriverTargets";
import { FileUsers } from "./FileUsers";
import { PackageMenu } from "./PackageMenu";
import { PackageName } from "./PackageName";
import type { PackageSummary } from "./packages";
import { packageContents } from "./packageView";
import type { PackageLibrary } from "./usePackageLibrary";

interface PackagesTableProps {
  items: PackageSummary[];
  drivers: boolean;
  library: PackageLibrary;
  now: number;
  canEdit: boolean;
  mark: (id: string) => string;
  // Both must stay the same across renders, because the rows keep them.
  onEdit: (item: PackageSummary) => void;
  onDelete: (item: PackageSummary) => void;
}

export function PackagesTable(props: PackagesTableProps) {
  const { items, drivers, library, now, canEdit, mark, onEdit, onDelete } = props;
  const { t: translate } = useLingui();

  return (
    <Table
      aria-label={drivers ? translate`Driver packages` : translate`File packages`}
      className="min-w-[860px] table-fixed"
    >
      <TableHeader>
        <TableColumn id="name" isRowHeader className="w-[32%] pl-4">
          <Trans>Package</Trans>
        </TableColumn>
        <TableColumn id="use">{drivers ? <Trans>For</Trans> : <Trans>Used by</Trans>}</TableColumn>
        <TableColumn id="size" className="w-36">
          <Trans>Size</Trans>
        </TableColumn>
        <TableColumn id="uploaded" className="w-44">
          <Trans>Uploaded</Trans>
        </TableColumn>
        <TableColumn id="actions" className="w-14 pr-4">
          <span className="sr-only">
            <Trans>Actions</Trans>
          </span>
        </TableColumn>
      </TableHeader>
      <TableBody items={items} dependencies={[now, canEdit, library.models, library.usersOf, mark]}>
        {(item) => (
          <TableRow id={item.id} textValue={item.name} className={mark(item.id)}>
            <TableCell className="pl-4">
              <PackageName item={item} />
            </TableCell>
            <TableCell className="type-small">
              {drivers ? (
                <DriverTargets item={item} matches={library.matchesOf(item)} />
              ) : (
                <FileUsers users={library.usersOf(item)} />
              )}
            </TableCell>
            <TableCell className="type-small">
              <span className="flex flex-col">
                <span>{formatBytes(item.sizeBytes)}</span>
                <span className="text-muted">{packageContents(item)}</span>
              </span>
            </TableCell>
            <TableCell className="type-small text-muted">
              <Uploaded entry={item} now={now} />
            </TableCell>
            <TableCell className="pr-4">
              {canEdit ? (
                <PackageMenu
                  item={item}
                  onEdit={() => {
                    onEdit(item);
                  }}
                  onDelete={() => {
                    onDelete(item);
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
