// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";

import { cx } from "@/ui/cx";
import { Table, TableBody, TableCell, TableColumn, TableHeader, TableRow } from "@/ui/Table";

import type { ApplicationBundleView } from "../applications";
import { EntryMenu } from "../list/EntryMenu";
import { UsedBy } from "../list/UsedBy";

interface BundlesTableProps {
  list: readonly ApplicationBundleView[];
  canEdit: boolean;
  mark: (id: string) => string;
  nameOf: (id: string) => string | null;
  onOpen: (bundle: ApplicationBundleView) => void;
  onDelete: (bundle: ApplicationBundleView) => void;
}

export function BundlesTable(props: BundlesTableProps) {
  const { list, canEdit, mark, nameOf, onOpen, onDelete } = props;
  const { t } = useLingui();

  return (
    <Table
      aria-label={t`Bundles`}
      className="min-w-[760px] table-fixed"
      {...(canEdit
        ? {
            onRowAction: (key) => {
              const bundle = list.find((candidate) => candidate.id === String(key));

              if (bundle !== undefined) {
                onOpen(bundle);
              }
            },
          }
        : {})}
    >
      <TableHeader>
        <TableColumn id="name" isRowHeader className="w-[30%] pl-4">
          <Trans>Bundle</Trans>
        </TableColumn>
        <TableColumn id="members" className="w-[36%]">
          <Trans>Applications</Trans>
        </TableColumn>
        <TableColumn id="usedBy">
          <Trans>Used by</Trans>
        </TableColumn>
        <TableColumn id="actions" className="w-14 pr-4">
          <span className="sr-only">
            <Trans>Actions</Trans>
          </span>
        </TableColumn>
      </TableHeader>
      <TableBody items={list} dependencies={[mark, canEdit, nameOf]}>
        {(bundle) => {
          const count = bundle.members.length;
          const names = bundle.members.map((id) => nameOf(id) ?? "?").join(", ");

          return (
            <TableRow
              id={bundle.id}
              textValue={bundle.name}
              className={cx("h-auto", mark(bundle.id))}
            >
              <TableCell className="py-3 pl-4">
                <span className="flex min-w-0 flex-col">
                  <span className="truncate type-label">{bundle.name}</span>
                  {bundle.description !== null ? (
                    <span className="truncate type-small text-muted">{bundle.description}</span>
                  ) : null}
                </span>
              </TableCell>
              <TableCell className="py-3 type-small">
                <span className="flex min-w-0 flex-col">
                  <span>{plural(count, { one: "# application", other: "# applications" })}</span>
                  <span className="truncate text-muted">{names}</span>
                </span>
              </TableCell>
              <TableCell className="py-3 type-small">
                <UsedBy uses={bundle.usedBy} />
              </TableCell>
              <TableCell className="pr-4">
                {canEdit ? (
                  <EntryMenu
                    name={bundle.name}
                    onEdit={() => {
                      onOpen(bundle);
                    }}
                    onDelete={() => {
                      onDelete(bundle);
                    }}
                  />
                ) : null}
              </TableCell>
            </TableRow>
          );
        }}
      </TableBody>
    </Table>
  );
}
