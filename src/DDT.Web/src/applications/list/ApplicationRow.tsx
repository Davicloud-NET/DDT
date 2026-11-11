// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { formatBytes } from "@/lib/format";
import { cx } from "@/ui/cx";
import { TableCell, TableRow } from "@/ui/Table";

import type { ApplicationView } from "../applications";
import { kindLabel } from "../applicationView";
import { EntryMenu } from "./EntryMenu";
import { UsedBy } from "./UsedBy";

interface ApplicationRowProps {
  application: ApplicationView;
  canEdit: boolean;
  // The classes that mark the row when a live change arrives.
  mark: string;
  onOpen: () => void;
  onDelete: () => void;
}

export function ApplicationRow({
  application,
  canEdit,
  mark,
  onOpen,
  onDelete,
}: ApplicationRowProps) {
  const { files } = application;

  return (
    <TableRow id={application.id} textValue={application.name} className={cx("h-auto", mark)}>
      <TableCell className="py-3 pl-4">
        <span className="flex min-w-0 flex-col">
          <span className="truncate type-label">{application.name}</span>
          {application.description !== null ? (
            <span className="truncate type-small text-muted">{application.description}</span>
          ) : null}
        </span>
      </TableCell>
      <TableCell className="py-3 type-small">
        <span className="block truncate">{application.publisher}</span>
      </TableCell>
      <TableCell className="py-3 type-data">
        <span className="block truncate">{application.version}</span>
      </TableCell>
      <TableCell className="py-3 type-small">
        <span className="flex min-w-0 flex-col">
          <span>{kindLabel(application.kind)}</span>
          {application.wingetId !== null ? (
            <span className="truncate type-data text-muted">{application.wingetId}</span>
          ) : null}
        </span>
      </TableCell>
      <TableCell className="py-3 type-small">
        {files === null ? (
          <span className="text-muted">
            <Trans>None</Trans>
          </span>
        ) : (
          <span className="flex min-w-0 flex-col">
            <span>{formatBytes(files.sizeBytes)}</span>
            <span className="truncate text-muted">{files.name}</span>
          </span>
        )}
      </TableCell>
      <TableCell className="py-3 type-small">
        <UsedBy uses={application.usedBy} />
      </TableCell>
      <TableCell className="pr-4">
        {canEdit ? <EntryMenu name={application.name} onEdit={onOpen} onDelete={onDelete} /> : null}
      </TableCell>
    </TableRow>
  );
}
