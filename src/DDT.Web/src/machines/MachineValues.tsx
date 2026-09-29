// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import type { ReactNode } from "react";

import { cx } from "@/ui/cx";
import { Panel } from "@/ui/Panel";
import type { ValueRow } from "@/values/valueRows";

// The values of a run, or the values a run would start with. Each row has the name in the mono face, the value, and
// where it came from. A secret only shows that it was given.
export function MachineValues({
  title,
  rows,
  empty,
  className,
}: {
  title: ReactNode;
  rows: readonly ValueRow[];
  // What the panel says when there are none.
  empty: ReactNode;
  className?: string;
}) {
  return (
    <Panel title={title} {...(className === undefined ? {} : { className })}>
      {rows.length === 0 ? (
        <p className="type-small text-muted">{empty}</p>
      ) : (
        <dl className="-mt-1 flex flex-col">
          {rows.map((row) => (
            <div
              key={row.name}
              className="grid grid-cols-[minmax(7rem,9.25rem)_minmax(0,1fr)] gap-3 border-b border-line-soft py-2 last:border-b-0"
            >
              <dt className="truncate type-data leading-6 text-ink-2" title={row.name}>
                {row.name}
              </dt>
              <dd className="flex min-w-0 flex-col gap-0.5">
                <span
                  className={cx("truncate", row.value === null ? "text-ink-2" : "text-ink")}
                  title={row.value ?? undefined}
                >
                  {row.value ?? secretText()}
                </span>
                <span className="type-small text-muted">{row.source}</span>
              </dd>
            </div>
          ))}
        </dl>
      )}
    </Panel>
  );
}

function secretText(): string {
  return t`Given, never shown`;
}
