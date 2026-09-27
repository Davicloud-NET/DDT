// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

import { cx } from "./cx";

// The one place a state gets its look. Filled tags are the states that ask for attention or show work in progress;
// the resting states are outlined, so a list of finished machines stays quiet. Tags are printed, not raised: flat,
// square and in capitals, the only capitals in the interface. A tag whose state changes turns to its new colours in
// fast.
export type StateTone = "run" | "attention" | "fail" | "ok" | "idle" | "retired";

const tones: Record<StateTone, string> = {
  run: "bg-run text-white",
  attention: "bg-attention text-on-attention",
  fail: "bg-fail text-on-fail",
  ok: "text-ok-text shadow-[inset_0_0_0_1.5px_var(--color-ok-text)]",
  idle: "text-ink-2 shadow-[inset_0_0_0_1px_var(--color-control)]",
  retired: "text-muted shadow-[inset_0_0_0_1px_var(--color-line)]",
};

export function StateTag({
  tone,
  children,
  className,
}: {
  tone: StateTone;
  children: ReactNode;
  className?: string;
}) {
  return (
    <span
      className={cx(
        "inline-flex h-6 shrink-0 items-center self-start rounded-tag px-2 whitespace-nowrap type-tag motion-colors",
        tones[tone],
        className,
      )}
    >
      {children}
    </span>
  );
}
