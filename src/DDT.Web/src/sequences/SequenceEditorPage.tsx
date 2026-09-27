// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { IconChevronLeft } from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { Link, useParams } from "@tanstack/react-router";

import { currentUserQuery } from "@/auth/auth";
import { ApiError } from "@/lib/api";
import { liveListOptions } from "@/live/freshness";
import { useLiveStatus } from "@/live/useLiveStatus";
import { EmptyState, Page, Skeleton } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";

import { SequenceEditor } from "./SequenceEditor";
import { sequenceQuery } from "./sequences";

// One task sequence, edited in place by administrators and read by everyone else.
export function SequenceEditorPage() {
  const { sequenceId } = useParams({ from: "/shell/deployment/sequences/$sequenceId" });
  const sequence = useQuery({ ...sequenceQuery(sequenceId), ...liveListOptions(useLiveStatus()) });
  const user = useQuery(currentUserQuery).data ?? null;

  const isAdministrator = user?.roles.includes("Administrator") === true;
  // The editor keeps its copy once open, and says itself when the sequence goes away.
  const missing =
    sequence.data === undefined &&
    sequence.error instanceof ApiError &&
    sequence.error.status === 404;

  return (
    <Page>
      <Link
        to="/deployment/sequences"
        className="-mb-1 flex w-fit items-center gap-1 type-small text-ink-2 hover:text-ink hover:underline"
      >
        <IconChevronLeft size={16} stroke={2} aria-hidden="true" />
        <Trans>Task sequences</Trans>
      </Link>

      {missing ? (
        <EmptyState title={<Trans>Sequence not found</Trans>}>
          <Trans>This sequence does not exist. It may have been deleted.</Trans>
        </EmptyState>
      ) : null}

      {!missing && sequence.isError && sequence.data === undefined ? (
        <Notice tone="fail">
          <Trans>The sequence could not be loaded.</Trans>
        </Notice>
      ) : null}

      {sequence.isPending ? <EditorSkeleton /> : null}

      {sequence.data !== undefined && user !== null ? (
        <SequenceEditor key={sequenceId} initial={sequence.data} readOnly={!isAdministrator} />
      ) : null}
    </Page>
  );
}

function EditorSkeleton() {
  return (
    <div aria-hidden="true" className="flex flex-col gap-4">
      <Skeleton className="h-10 w-80" />
      <Skeleton className="h-28 w-full" />
      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_24rem]">
        <Skeleton className="h-96" />
        <Skeleton className="h-60" />
      </div>
    </div>
  );
}
