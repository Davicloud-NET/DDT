// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { describeTarget, type PackageSummary } from "./packages";

// The models a driver package is for, and how many registered machines report one of them.
export function DriverTargets({ item, matches: count }: { item: PackageSummary; matches: number }) {
  if (item.targets.length === 0) {
    return (
      <span className="text-attention-text">
        <Trans>No model yet: no machine gets these drivers.</Trans>
      </span>
    );
  }

  const targets = item.targets.map(describeTarget).join(", ");

  return (
    <span className="flex min-w-0 flex-col">
      <span className="truncate text-ink">{targets}</span>
      <span className="text-muted">
        {count === 0
          ? t`No registered machine matches yet.`
          : plural(count, {
              one: "Matches # registered machine.",
              other: "Matches # registered machines.",
            })}
      </span>
    </span>
  );
}
