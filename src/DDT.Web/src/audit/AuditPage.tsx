// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useInfiniteQuery } from "@tanstack/react-query";

import { useIsAdministrator } from "@/auth/useIsAdministrator";
import { useLiveMarks } from "@/live/useLiveMarks";
import { Button } from "@/ui/Button";
import { EmptyState } from "@/ui/EmptyState";
import { ListSkeleton } from "@/ui/ListSkeleton";
import { Notice } from "@/ui/Notice";
import { Page } from "@/ui/Page";
import { PageHeader } from "@/ui/PageHeader";
import { Panel } from "@/ui/Panel";

import { auditQuery } from "./audit";
import { AuditFilters } from "./AuditFilters";
import { AuditTable } from "./AuditTable";
import { useAuditFilter } from "./useAuditFilter";

// Who did what and when: every row of the audit table, newest first, a page at a time. New rows reach the page as
// they are stored, in the logs whose filter they pass, and enter at the top.
export function AuditPage() {
  const fields = useAuditFilter();
  const filter = fields.filter;
  const administrator = useIsAdministrator();
  const log = useInfiniteQuery({ ...auditQuery(filter), enabled: administrator });
  const mark = useLiveMarks({
    queryKey: auditQuery(filter).queryKey,
    items: (data) => data.pages.flatMap((page) => page.items),
    id: (entry) => String(entry.id),
    signature: () => "",
    tone: () => "idle",
  });
  const entries = log.data?.pages.flatMap((page) => page.items) ?? [];
  const readError = log.error?.message ?? "";

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

      <AuditFilters fields={fields} />

      {log.isError ? (
        <Notice tone="fail">
          <Trans>The audit log could not be read: {readError}</Trans>
        </Notice>
      ) : null}

      <Panel flush>
        {log.isPending ? (
          <ListSkeleton widths={["w-2/3", "w-1/2", "w-3/4"]} />
        ) : entries.length === 0 ? (
          <EmptyState
            title={
              fields.filtered ? (
                <Trans>No entry matches</Trans>
              ) : (
                <Trans>Nothing recorded yet</Trans>
              )
            }
          />
        ) : (
          <AuditTable entries={entries} mark={mark} />
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
