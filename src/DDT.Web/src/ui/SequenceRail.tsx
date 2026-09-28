// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

import { cx } from "./cx";

// The sequence rail: one module per step of a task sequence. It is the same part at every size: a thin strip in a
// table row, a labelled row on a run's page, large on the console in Windows PE, and along the top of a node in a
// flow. A paused step waits for someone, in the signal colour for that.
export type RailStepState = "done" | "running" | "failed" | "skipped" | "waiting" | "paused";

export interface RailStep {
  state: RailStepState;
  // How far the running step is, from 0 to 100.
  percent?: number;
  name?: ReactNode;
  // A short line under the number, such as the step's duration or its percentage.
  meta?: ReactNode;
  // A finding on a step of a sequence being edited: a problem keeps the sequence from running, a warning does not.
  mark?: "problem" | "warning";
  // The step's number in its sequence, where it is not its place on the rail: a run's rail leaves out the steps of the
  // branches it did not take.
  number?: number;
}

export interface RailPhase {
  label: ReactNode;
  // How many consecutive steps, from the previous phase's end, belong to this phase.
  steps: number;
}

// One fill serves the running and the done step, so a step that moves on or finishes fills on in slow, and turns
// from the running blue to done, instead of jumping. The stripes run only while the step does. className sets the
// module's height, and its corners where it sits on something else's edge.
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

function columns(count: number) {
  return { gridTemplateColumns: `repeat(${String(Math.max(count, 1))}, minmax(0, 1fr))` };
}

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
      style={columns(steps.length)}
    >
      {steps.map((step, index) => (
        <RailModule key={index} step={step} className="h-2" />
      ))}
    </span>
  );
}

// The labelled rail of a run's page, optionally with the phases the steps run in drawn above them.
export function SequenceRail({
  steps,
  phases,
  size = "md",
  showNames = true,
  describe,
  className,
}: {
  steps: RailStep[];
  phases?: RailPhase[];
  size?: "md" | "lg";
  // Without names, only the number and the short line show, for narrow places such as a panel.
  showNames?: boolean;
  // Says a step in words for screen readers, such as "Step 2, Apply image, running, 62 percent".
  describe: (step: RailStep, index: number) => string;
  className?: string;
}) {
  const large = size === "lg";

  return (
    <div className={cx("flex flex-col gap-2", className)}>
      {phases && phases.length > 0 ? (
        <div
          aria-hidden="true"
          className="grid gap-1.5"
          style={{
            gridTemplateColumns: phases.map((phase) => `${String(phase.steps)}fr`).join(" "),
          }}
        >
          {phases.map((phase, index) => (
            <span
              key={index}
              className={cx(
                "border-b border-control pb-1 text-ink-2",
                large ? "type-body" : "type-small",
              )}
            >
              {phase.label}
            </span>
          ))}
        </div>
      ) : null}
      <ol className="grid gap-1.5" style={columns(steps.length)}>
        {steps.map((step, index) => {
          const running = step.state === "running";

          return (
            <li key={index} className="flex min-w-0 flex-col gap-2">
              <span className="sr-only">{describe(step, index)}</span>
              <RailModule step={step} className={large ? "h-5.5" : "h-4"} />
              <span aria-hidden="true" className="flex items-baseline gap-2">
                <span
                  className={cx(
                    "type-numeral",
                    running
                      ? "text-run-text"
                      : step.state === "waiting"
                        ? "text-muted"
                        : "text-ink",
                  )}
                >
                  {String(step.number ?? index + 1).padStart(2, "0")}
                </span>
                {step.meta ? (
                  <span
                    className={cx(
                      "type-small",
                      running
                        ? "font-semibold text-run-text"
                        : step.state === "paused"
                          ? "font-semibold text-attention-text"
                          : "text-muted",
                    )}
                  >
                    {step.meta}
                  </span>
                ) : null}
              </span>
              {showNames && step.name ? (
                <span
                  aria-hidden="true"
                  className={cx(
                    "type-step",
                    running || step.state === "paused"
                      ? "font-bold text-ink"
                      : step.state === "waiting"
                        ? "text-ink-2"
                        : "text-ink",
                  )}
                >
                  {step.name}
                </span>
              ) : null}
            </li>
          );
        })}
      </ol>
    </div>
  );
}
