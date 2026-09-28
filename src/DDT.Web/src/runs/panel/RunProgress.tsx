// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { DeploymentSummary } from "@/deployments/deployments";

import { pathPercent, reachedCount, type RunPath } from "../runPath";

// How far the run is: the steps it reached on a tree's path, else its percentage while it runs.
export function RunProgress({ run, path }: { run: DeploymentSummary; path: RunPath | null }) {
  const count = path?.leaves.length ?? 0;
  const reached = path === null ? 0 : reachedCount(path);
  const overall = path !== null ? pathPercent(path) : run.state === "Done" ? 100 : null;

  return path?.isTree === true && count > 0 ? (
    <span className="flex items-baseline gap-2 pt-1 type-small text-muted">
      <Trans>
        <span className="type-numeral text-ink">
          {reached} of {count}
        </span>{" "}
        steps on this path
      </Trans>
    </span>
  ) : overall !== null && run.state === "Running" ? (
    <span className="flex items-baseline gap-2 pt-1">
      <span className="type-numeral text-ink">{overall}%</span>
      <span className="type-small text-muted">
        <Trans>of the run</Trans>
      </span>
    </span>
  ) : null;
}
