// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

import { cx } from "./cx";

// A page's content area: the same gutters on every page.
export function Page({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div className={cx("flex flex-col gap-4 px-4 pt-5 pb-8 sm:px-6", className)}>{children}</div>
  );
}
