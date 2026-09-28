// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

import { cx } from "./cx";
import { RailModule } from "./RailModule";
import { railColumns } from "./railColumns";

// The sequence rail has one module per step, the same part in a table row, on a run's page, on the console in
// Windows PE and along the top of a flow's node. A paused step waits for someone, so it takes the signal colour.
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
      <ol className="grid gap-1.5" style={railColumns(steps.length)}>
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
