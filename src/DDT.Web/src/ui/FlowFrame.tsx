// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { CSSProperties } from "react";

import { cx } from "./cx";

// The frame of a group or a repeat, around its header card and its body.
export function FlowFrame({ className, style }: { className?: string; style?: CSSProperties }) {
  return (
    <div
      aria-hidden="true"
      className={cx("rounded-panel bg-panel shadow-[inset_0_0_0_1px_var(--color-line)]", className)}
      style={style}
    />
  );
}
