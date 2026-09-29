// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

import { cx } from "./cx";

// A raised surface, one step lighter than the page, edged with a hairline. Panels cast no shadow.
export function Panel({
  children,
  title,
  actions,
  className,
  flush = false,
}: {
  children: ReactNode;
  title?: ReactNode;
  actions?: ReactNode;
  className?: string;
  // Without inner padding, for tables and lists that run to the panel's edges.
  flush?: boolean;
}) {
  return (
    <section className={cx("flex flex-col rounded-panel bg-panel shadow-panel", className)}>
      {title || actions ? (
        <div
          className={cx(
            "flex items-center gap-3",
            flush ? "border-b border-line-soft px-4 py-3" : "px-4 pt-4",
          )}
        >
          {title ? (
            <h2 className="flex-1 type-heading text-ink">{title}</h2>
          ) : (
            <span className="flex-1" />
          )}
          {actions}
        </div>
      ) : null}
      <div className={cx("flex min-h-0 flex-1 flex-col", !flush && "gap-3 p-4")}>{children}</div>
    </section>
  );
}
