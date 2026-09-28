// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { Link } from "@tanstack/react-router";

// The sequences that name a file package, as links; users is null while not every sequence is read.
export function FileUsers({ users }: { users: readonly { id: string; name: string }[] | null }) {
  if (users === null) {
    return <span className="text-muted">…</span>;
  }

  if (users.length === 0) {
    return (
      <span className="text-muted">
        <Trans>No sequence names it</Trans>
      </span>
    );
  }

  return (
    <span className="flex min-w-0 flex-wrap gap-x-2">
      {users.map((sequence) => (
        <Link
          key={sequence.id}
          to="/deployment/sequences/$sequenceId"
          params={{ sequenceId: sequence.id }}
          className="truncate text-ink hover:underline"
        >
          {sequence.name}
        </Link>
      ))}
    </span>
  );
}
