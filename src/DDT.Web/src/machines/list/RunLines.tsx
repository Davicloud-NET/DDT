// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { cx } from "@/ui/cx";

import type { RunDetailTone } from "./runDetail";

// Title and detail share a line when the column is wide. When the details panel is open, the detail goes under the
// title.
export function RunLines({
  title,
  detail,
  detailTone,
}: {
  title: string;
  detail: string | null;
  detailTone: RunDetailTone;
}) {
  return (
    <span className="@container flex min-w-0">
      <span className="flex min-w-0 flex-1 flex-col @sm:flex-row @sm:items-baseline @sm:gap-3.5">
        <span className="truncate">{title}</span>
        <span className="hidden flex-1 @sm:block" />
        {detail !== null ? (
          <span
            className={cx(
              "truncate type-small @sm:shrink-0 @sm:overflow-visible",
              detailTone === "muted" && "text-muted",
              detailTone === "ink" && "text-ink",
              detailTone === "run" && "font-semibold text-run-text",
              detailTone === "fail" && "font-semibold text-fail-text",
              detailTone === "attention" && "font-semibold text-attention-text",
            )}
          >
            {detail}
          </span>
        ) : null}
      </span>
    </span>
  );
}
