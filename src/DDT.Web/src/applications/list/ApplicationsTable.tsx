// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { Table, TableBody, TableColumn, TableHeader } from "@/ui/Table";

import type { ApplicationView } from "../applications";
import { ApplicationRow } from "./ApplicationRow";

interface ApplicationsTableProps {
  list: readonly ApplicationView[];
  canEdit: boolean;
  mark: (id: string) => string;
  onOpen: (application: ApplicationView) => void;
  onDelete: (application: ApplicationView) => void;
}

// Only someone who may change the library opens an application.
export function ApplicationsTable({
  list,
  canEdit,
  mark,
  onOpen,
  onDelete,
}: ApplicationsTableProps) {
  const { t } = useLingui();

  return (
    <Table
      aria-label={t`Applications`}
      className="min-w-[960px] table-fixed"
      {...(canEdit
        ? {
            onRowAction: (key) => {
              const application = list.find((candidate) => candidate.id === String(key));

              if (application !== undefined) {
                onOpen(application);
              }
            },
          }
        : {})}
    >
      <TableHeader>
        <TableColumn id="name" isRowHeader className="w-[24%] pl-4">
          <Trans>Application</Trans>
        </TableColumn>
        <TableColumn id="publisher" className="w-[14%]">
          <Trans>Publisher</Trans>
        </TableColumn>
        <TableColumn id="version" className="w-[10%]">
          <Trans>Version</Trans>
        </TableColumn>
        <TableColumn id="kind" className="w-[13%]">
          <Trans>Kind</Trans>
        </TableColumn>
        <TableColumn id="files" className="w-[14%]">
          <Trans>Files</Trans>
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
      <TableBody items={list} dependencies={[mark, canEdit]}>
        {(application) => (
          <ApplicationRow
            application={application}
            canEdit={canEdit}
            mark={mark(application.id)}
            onOpen={() => {
              onOpen(application);
            }}
            onDelete={() => {
              onDelete(application);
            }}
          />
        )}
      </TableBody>
    </Table>
  );
}
