// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { cx } from "./cx";
import { RailModule } from "./RailModule";
import { railColumns } from "./railColumns";
import type { RailStep } from "./SequenceRail";

// A strip for table rows. `label` says the same in words, for screen readers and as a tooltip.
export function SequenceRailStrip({
  steps,
  label,
  className,
}: {
  steps: RailStep[];
  label: string;
  className?: string;
}) {
  return (
    <span
      role="img"
      aria-label={label}
      title={label}
      className={cx("grid gap-0.75", className)}
      style={railColumns(steps.length)}
    >
      {steps.map((step, index) => (
        <RailModule key={index} step={step} className="h-2" />
      ))}
    </span>
  );
}
