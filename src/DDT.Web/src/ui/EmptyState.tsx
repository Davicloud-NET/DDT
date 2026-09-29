// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

import { cx } from "./cx";

// What a place shows when there is nothing in it yet: what it will hold, and how to put something there.
export function EmptyState({
  title,
  children,
  action,
  className,
}: {
  title: ReactNode;
  children?: ReactNode;
  action?: ReactNode;
  className?: string;
}) {
  return (
    <div className={cx("flex flex-col items-start gap-2 px-4 py-8", className)}>
      <p className="type-heading text-ink">{title}</p>
      {children ? <p className="max-w-[60ch] text-ink-2">{children}</p> : null}
      {action ? <div className="pt-2">{action}</div> : null}
    </div>
  );
}
