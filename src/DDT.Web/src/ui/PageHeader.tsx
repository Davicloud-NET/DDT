// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

import { cx } from "./cx";

// The page title with what sits beside it: filters, a search field, the page's main action.
export function PageHeader({
  title,
  children,
  className,
}: {
  title: ReactNode;
  children?: ReactNode;
  className?: string;
}) {
  return (
    <div className={cx("flex flex-wrap items-center gap-x-5 gap-y-3", className)}>
      <h1 className="type-title text-ink">{title}</h1>
      {children}
    </div>
  );
}
