// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { Link } from "@tanstack/react-router";

import type { ApplicationUse } from "../applications";
import { usageLabel } from "../applicationView";

// The bundles, applications and sequences that name an entry. A sequence links to its page.
export function UsedBy({ uses }: { uses: readonly ApplicationUse[] }) {
  if (uses.length === 0) {
    return (
      <span className="text-muted">
        <Trans>Not used</Trans>
      </span>
    );
  }

  return (
    <ul className="flex min-w-0 flex-col gap-0.5">
      {uses.map((use) => (
        <li key={`${use.kind} ${use.id}`} className="min-w-0 truncate">
          {use.kind === "Sequence" ? (
            <Link
              to="/deployment/sequences/$sequenceId"
              params={{ sequenceId: use.id }}
              className="text-ink hover:underline"
            >
              {use.name}
            </Link>
          ) : (
            <span className="text-ink">{use.name}</span>
          )}
          <span className="text-muted"> · {usageLabel(use.kind)}</span>
        </li>
      ))}
    </ul>
  );
}
