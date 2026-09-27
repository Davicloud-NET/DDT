// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { useEffect, useState } from "react";

import { currentUserQuery } from "@/auth/auth";
import { formattingLocale } from "@/i18n/i18n";
import { useLiveMarks } from "@/live/useLiveMarks";
import { Button } from "@/ui/Button";
import { SearchField } from "@/ui/Controls";
import { EmptyState, Page, PageHeader, Panel, Skeleton } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";
import { ListBoxItem, Select } from "@/ui/Select";
import { Table, TableBody, TableCell, TableColumn, TableHeader, TableRow } from "@/ui/Table";
import { TextField } from "@/ui/TextField";

import { auditQuery, type AuditFilter } from "./audit";
import { actionGroups, actionLabel, actorKindLabel } from "./auditView";

// Who did what and when: every row of the audit table, newest first, a page at a time. New rows reach the page as
// they are stored, in the logs whose filter they pass, and enter at the top.
export function AuditPage() {
  const { i18n, t } = useLingui();
  const user = useQuery(currentUserQuery).data ?? null;
  const [action, setAction] = useState("");
  const [typedActor, setTypedActor] = useState("");
  const [actor, setActor] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");

  // The actor is searched on the server, so it waits until typing pauses.
  useEffect(() => {
    const timer = window.setTimeout(() => {
      setActor(typedActor);
    }, 300);

    return () => {
      window.clearTimeout(timer);
    };
  }, [typedActor]);

  const administrator = user?.roles.includes("Administrator") === true;
  const filter: AuditFilter = { action, actor, from, to };
  const log = useInfiniteQuery({ ...auditQuery(filter), enabled: administrator });
  const mark = useLiveMarks({
    queryKey: auditQuery(filter).queryKey,
    items: (data) => data.pages.flatMap((page) => page.items),
    id: (entry) => String(entry.id),
    signature: () => "",
    tone: () => "idle",
  });
  const entries = log.data?.pages.flatMap((page) => page.items) ?? [];
  const locale = formattingLocale();
  const readError = log.error?.message ?? "";
  const filtered = action !== "" || actor !== "" || from !== "" || to !== "";

  if (!administrator) {
    return (
      <Page>
        <PageHeader title={<Trans>Audit log</Trans>} />
        <Notice tone="info">
          <Trans>Only administrators read the audit log.</Trans>
        </Notice>
      </Page>
    );
  }

  return (
    <Page>
      <PageHeader title={<Trans>Audit log</Trans>} />

      <div className="flex flex-wrap items-end gap-3">
        <Select
          label={<Trans>What</Trans>}
          value={action === "" ? "all" : action}
          onChange={(key) => {
            setAction(key === null || key === "all" ? "" : String(key));
          }}
          className="w-56"
        >
          {actionGroups.map((group) => (
            <ListBoxItem
              key={group.prefix}
              id={group.prefix === "" ? "all" : group.prefix}
              textValue={i18n._(group.label)}
            >
              {i18n._(group.label)}
            </ListBoxItem>
          ))}
        </Select>
        <SearchField
          label={t`Who`}
          placeholder={t`Any part of a name`}
          value={typedActor}
          onChange={setTypedActor}
          className="w-64"
        />
        <TextField
          label={<Trans>From</Trans>}
          type="date"
          value={from}
          onChange={setFrom}
          className="w-44"
        />
        <TextField
          label={<Trans>Until</Trans>}
          type="date"
          value={to}
          onChange={setTo}
          className="w-44"
        />
        {filtered ? (
          <Button
            variant="quiet"
            onPress={() => {
              setAction("");
              setTypedActor("");
              setActor("");
              setFrom("");
              setTo("");
            }}
          >
            <Trans>Show everything</Trans>
          </Button>
        ) : null}
      </div>

      {log.isError ? (
        <Notice tone="fail">
          <Trans>The audit log could not be read: {readError}</Trans>
        </Notice>
      ) : null}

      <Panel flush>
        {log.isPending ? (
          <div className="flex flex-col gap-3 p-4">
            <Skeleton className="h-6 w-2/3" />
            <Skeleton className="h-6 w-1/2" />
            <Skeleton className="h-6 w-3/4" />
          </div>
        ) : entries.length === 0 ? (
          <EmptyState
            title={filtered ? <Trans>No entry matches</Trans> : <Trans>Nothing recorded yet</Trans>}
          />
        ) : (
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
                      <span className="truncate type-label text-ink">
                        {actionLabel(entry.action)}
                      </span>
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
        )}
        {log.hasNextPage ? (
          <div className="border-t border-line-soft px-4 py-3">
            <Button
              isDisabled={log.isFetchingNextPage}
              onPress={() => {
                void log.fetchNextPage();
              }}
            >
              {log.isFetchingNextPage ? (
                <Trans>Loading older entries</Trans>
              ) : (
                <Trans>Load older entries</Trans>
              )}
            </Button>
          </div>
        ) : null}
      </Panel>
    </Page>
  );
}
