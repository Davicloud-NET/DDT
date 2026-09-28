// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { cx } from "./cx";

// A placeholder in the shape of what is loading, so the page does not jump when it arrives. It stands for work in
// progress, so it pulses, barely: most loads end before the first pulse shows.
export function Skeleton({ className }: { className?: string }) {
  return (
    <span
      aria-hidden="true"
      className={cx("block animate-skeleton rounded-tag bg-well", className)}
    />
  );
}
