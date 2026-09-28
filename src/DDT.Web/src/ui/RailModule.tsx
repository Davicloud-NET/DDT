// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { cx } from "./cx";
import type { RailStep } from "./SequenceRail";

// One fill serves the running and the done step, so a finishing step fills on and changes colour instead of
// jumping. className sets the height, and the corners where the module sits on another part's edge.
export function RailModule({ step, className }: { step: RailStep; className: string }) {
  const percent = Math.min(100, Math.max(0, step.percent ?? 0));
  const fill =
    step.state === "done" || step.state === "paused" ? 100 : step.state === "running" ? percent : 0;

  return (
    <span
      aria-hidden="true"
      className={cx(
        "relative block overflow-hidden rounded-tag bg-well shadow-[inset_0_0_0_1px_var(--color-rail-edge)]",
        step.state === "running" && "shadow-[0_0_0_1px_var(--color-run)]",
        className,
      )}
    >
      <span
        className={cx(
          "absolute inset-y-0 left-0 motion-fill",
          step.state === "done"
            ? "bg-rail-done"
            : step.state === "paused"
              ? "bg-attention"
              : "bg-run",
          step.state === "running" && "rail-live",
        )}
        style={{ width: `${String(fill)}%` }}
      />
      {step.state === "failed" ? <span className="absolute inset-0 hatch-fail" /> : null}
      {step.state === "skipped" ? <span className="absolute inset-0 hatch-skip" /> : null}
      {step.mark === "problem" ? <span className="absolute inset-0 hatch-fail" /> : null}
      {step.mark === "warning" ? <span className="absolute inset-0 bg-attention" /> : null}
    </span>
  );
}
