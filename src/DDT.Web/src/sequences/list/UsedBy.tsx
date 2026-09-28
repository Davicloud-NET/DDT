// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

// The rules that choose the sequence, and the machines it is assigned to or running on now.
export function UsedBy({ targets, activeRuns }: { targets: string[]; activeRuns: number }) {
  if (targets.length === 0 && activeRuns === 0) {
    return (
      <span className="type-small text-muted">
        <Trans>No rule, no machine</Trans>
      </span>
    );
  }

  return (
    <span className="flex min-w-0 flex-col type-small">
      {targets.map((target) => (
        <span key={target} className="truncate text-ink">
          {target}
        </span>
      ))}
      {activeRuns > 0 ? (
        <span className="text-run-text">
          {plural(activeRuns, {
            one: "Assigned to or running on # machine",
            other: "Assigned to or running on # machines",
          })}
        </span>
      ) : null}
    </span>
  );
}
