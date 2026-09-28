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

import { FlowBuilder } from "./builder/FlowBuilder";
import { sequenceQuery } from "./sequences";

// One task sequence, edited in place as a flow by administrators and read by everyone else. The route loads it on its
// own, with the flow builder.
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
    <Page className="h-full">
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
        <FlowBuilder key={sequenceId} initial={sequence.data} readOnly={!isAdministrator} />
      ) : null}
    </Page>
  );
}

function EditorSkeleton() {
  return (
    <div aria-hidden="true" className="flex flex-col gap-4">
      <Skeleton className="h-10 w-80" />
      <div className="flex gap-4">
        <Skeleton className="hidden h-[36rem] w-54 xl:block" />
        <Skeleton className="h-[36rem] flex-1" />
        <Skeleton className="hidden h-[36rem] w-92 md:block" />
      </div>
    </div>
  );
}
