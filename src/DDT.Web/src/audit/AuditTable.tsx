// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { formattingLocale } from "@/i18n/i18n";
import { Table, TableBody, TableCell, TableColumn, TableHeader, TableRow } from "@/ui/Table";

import type { AuditEntry } from "./audit";
import { actionLabel, actorKindLabel } from "./auditView";

// mark returns the classes of a row the hub brought in (see useLiveMarks), so the rows list it in their dependencies.
export function AuditTable({
  entries,
  mark,
}: {
  entries: AuditEntry[];
  mark: (id: string) => string;
}) {
  const { i18n, t } = useLingui();
  const locale = formattingLocale();

  return (
    <Table aria-label={t`Audit log`} className="min-w-[960px] table-fixed">
      <TableHeader>
        <TableColumn id="when" isRowHeader className="w-44 pl-4">
          <Trans>When</Trans>
        </TableColumn>
        <TableColumn id="what" className="w-56">
          <Trans>What</Trans>
        </TableColumn>
        <TableColumn id="who" className="w-56">
          <Trans>Who</Trans>
        </TableColumn>
        <TableColumn id="detail" className="pr-4">
          <Trans>Detail</Trans>
        </TableColumn>
      </TableHeader>
      <TableBody items={entries} dependencies={[i18n.locale, mark]}>
        {(entry) => (
          <TableRow id={entry.id} textValue={entry.action} className={mark(String(entry.id))}>
            <TableCell className="pl-4 type-small">
              <time dateTime={entry.occurredUtc}>
                {new Date(entry.occurredUtc).toLocaleString(locale)}
              </time>
            </TableCell>
            <TableCell>
              <span className="flex min-w-0 flex-col">
                <span className="truncate type-label text-ink">{actionLabel(entry.action)}</span>
                <span className="truncate type-data text-muted">{entry.action}</span>
              </span>
            </TableCell>
            <TableCell>
              <span className="flex min-w-0 flex-col">
                <span className="truncate text-ink">
                  {entry.actorName ?? actorKindLabel(entry.actorKind)}
                </span>
                <span className="truncate type-small text-muted">
                  {entry.sourceAddress === null
                    ? actorKindLabel(entry.actorKind)
                    : `${actorKindLabel(entry.actorKind)}, ${entry.sourceAddress}`}
                </span>
              </span>
            </TableCell>
            <TableCell className="pr-4 type-small text-ink-2">
              <span className="flex min-w-0 flex-col gap-0.5">
                {entry.detail !== null ? (
                  <span className="break-words whitespace-normal">{entry.detail}</span>
                ) : null}
                {entry.subjectId !== null ? (
                  <span className="truncate type-data text-muted">{entry.subjectId}</span>
                ) : null}
              </span>
            </TableCell>
          </TableRow>
        )}
      </TableBody>
    </Table>
  );
}
