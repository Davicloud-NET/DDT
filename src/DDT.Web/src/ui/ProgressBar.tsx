// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";
import { Label, ProgressBar as AriaProgressBar } from "react-aria-components";

import { cx } from "./cx";

// A bar for one quantity, such as an upload. A step of a run uses the sequence rail instead.
export function ProgressBar({
  label,
  value,
  valueLabel,
  className,
}: {
  label: ReactNode;
  // From 0 to 100; undefined while the amount is not known yet.
  value?: number;
  valueLabel?: string;
  className?: string;
}) {
  return (
    <AriaProgressBar
      {...(value === undefined ? { isIndeterminate: true } : { value })}
      {...(valueLabel === undefined ? {} : { valueLabel })}
      className={cx("flex flex-col gap-1.5", className)}
    >
      {({ percentage, valueText, isIndeterminate }) => (
        <>
          <span className="flex justify-between gap-3 type-small">
            <Label className="text-ink-2">{label}</Label>
            {isIndeterminate ? null : <span className="text-ink">{valueText}</span>}
          </span>
          <span className="relative block h-2 overflow-hidden rounded-tag bg-well shadow-[inset_0_0_0_1px_var(--color-rail-edge)]">
            <span
              className={cx(
                "absolute inset-y-0 left-0 bg-run rail-live",
                isIndeterminate ? "w-full opacity-60" : "motion-fill",
              )}
              style={isIndeterminate ? undefined : { width: `${String(percentage ?? 0)}%` }}
            />
          </span>
        </>
      )}
    </AriaProgressBar>
  );
}
