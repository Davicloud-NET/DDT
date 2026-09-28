// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import type { UseQueryResult } from "@tanstack/react-query";

import { Notice } from "@/ui/Notice";
import { Skeleton } from "@/ui/Skeleton";

import type { PxeHostInterfaces } from "../networkBoot";

import { HostInterfaces } from "./HostInterfaces";

// The interfaces that the network boot hosts reported. Each can be added to the list or removed.
export function ReportedInterfaces({
  hosts,
  entries,
  editable,
  onChange,
}: {
  hosts: UseQueryResult<PxeHostInterfaces[]>;
  entries: string[];
  editable: boolean;
  onChange: (entries: string[]) => void;
}) {
  const reported = hosts.data ?? [];

  if (hosts.isError) {
    return (
      <Notice tone="fail">
        <Trans>The interfaces the hosts found could not be read.</Trans>
      </Notice>
    );
  }

  if (hosts.isPending) {
    return <Skeleton className="h-16 w-full" />;
  }

  if (reported.length === 0) {
    return (
      <p className="type-small text-muted">
        <Trans>
          No host that runs network boot has reported its interfaces yet. A host reports them when
          it applies these settings without problems.
        </Trans>
      </p>
    );
  }

  return (
    <div className="grid gap-4 md:grid-cols-2">
      {reported.map((host) => (
        <HostInterfaces
          key={host.host}
          host={host}
          entries={entries}
          editable={editable}
          onChange={onChange}
        />
      ))}
    </div>
  );
}
