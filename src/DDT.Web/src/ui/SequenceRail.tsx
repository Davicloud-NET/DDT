// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";
import { ListBox, ListBoxItem, type KeyboardEvent } from "react-aria-components";

import { cx } from "./cx";

// The sequence rail: one module per step of a task sequence. It is the same part at every size: a thin strip in a
// table row, a labelled row on a run's page, and large on the console in Windows PE.
export type RailStepState = "done" | "running" | "failed" | "skipped" | "waiting";

export interface RailStep {
  state: RailStepState;
  // How far the running step is, from 0 to 100.
  percent?: number;
  name?: ReactNode;
  // A short line under the number, such as the step's duration or its percentage.
  meta?: ReactNode;
  // A finding on a step of a sequence being edited: a problem keeps the sequence from running, a warning does not.
  mark?: "problem" | "warning";
}

export interface RailPhase {
  label: ReactNode;
  // How many consecutive steps, from the previous phase's end, belong to this phase.
  steps: number;
}

// One fill serves the running and the done step, so a step that moves on or finishes fills on in slow, and turns
// from the running blue to done, instead of jumping. The stripes run only while the step does.
function Module({ step, height }: { step: RailStep; height: string }) {
  const percent = Math.min(100, Math.max(0, step.percent ?? 0));
  const fill = step.state === "done" ? 100 : step.state === "running" ? percent : 0;

  return (
    <span
      aria-hidden="true"
      className={cx(
        "relative block overflow-hidden rounded-tag bg-well shadow-[inset_0_0_0_1px_var(--color-rail-edge)]",
        height,
        step.state === "running" && "shadow-[0_0_0_1px_var(--color-run)]",
      )}
    >
      <span
        className={cx(
          "absolute inset-y-0 left-0 motion-fill",
          step.state === "done" ? "bg-rail-done" : "bg-run",
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
        <Module key={index} step={step} height="h-2" />
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
              <Module step={step} height={large ? "h-5.5" : "h-4"} />
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
                  {String(index + 1).padStart(2, "0")}
                </span>
                {step.meta ? (
                  <span
                    className={cx(
                      "type-small",
                      running ? "font-semibold text-run-text" : "text-muted",
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
                    running
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

export interface RailPickerStep extends RailStep {
  id: string;
  // Says the step in words, such as "Step 2, Apply image, 1 problem", for screen readers and typing to find it.
  label: string;
}

// The rail of a sequence being edited: every module a step to pick, the picked one raised out of the rail like the
// chosen key of a filter. The rail scrolls sideways once the steps no longer fit. Alt with an arrow key moves the
// focused step, where onMove is given.
export function SequenceRailPicker({
  steps,
  phases,
  selectedId,
  onSelect,
  onMove,
  label,
  className,
}: {
  steps: RailPickerStep[];
  phases?: RailPhase[];
  selectedId: string | null;
  onSelect: (id: string) => void;
  onMove?: (id: string, to: number) => void;
  label: string;
  className?: string;
}) {
  // The phases sit in the same grid as the steps, so each label spans exactly the modules of its phase.
  const grid = {
    gridTemplateColumns: `repeat(${String(Math.max(steps.length, 1))}, minmax(7.5rem, 1fr))`,
  };

  function moveKey(event: KeyboardEvent, index: number, id: string) {
    const earlier = event.key === "ArrowLeft" || event.key === "ArrowUp";
    const later = event.key === "ArrowRight" || event.key === "ArrowDown";

    if (onMove === undefined || !event.altKey || !(earlier || later)) {
      event.continuePropagation();
      return;
    }

    const to = earlier ? index - 1 : index + 1;

    if (to >= 0 && to < steps.length) {
      onMove(id, to);
    }
  }

  return (
    <div className={cx("overflow-x-auto", className)}>
      <div className="grid min-w-full gap-x-1.5 gap-y-2" style={grid}>
        {phases && phases.length > 0
          ? phases.map((phase, index) => (
              <span
                key={index}
                aria-hidden="true"
                className="border-b border-control pb-1 type-small text-ink-2"
                style={{ gridColumn: `span ${String(Math.max(phase.steps, 1))}` }}
              >
                {phase.label}
              </span>
            ))
          : null}
        <ListBox
          aria-label={label}
          orientation="horizontal"
          selectionMode="single"
          disallowEmptySelection
          selectedKeys={selectedId === null ? [] : [selectedId]}
          onSelectionChange={(keys) => {
            const [key] = keys === "all" ? [] : [...keys];

            if (key !== undefined) {
              onSelect(String(key));
            }
          }}
          items={steps}
          className="col-span-full grid gap-x-1.5 outline-none"
          style={grid}
        >
          {(step) => {
            const index = steps.indexOf(step);

            return (
              <ListBoxItem
                id={step.id}
                textValue={step.label}
                aria-label={step.label}
                onKeyDown={(event) => {
                  moveKey(event, index, step.id);
                }}
                className={cx(
                  "group flex min-w-0 cursor-pointer flex-col gap-2 rounded-key p-1.5 motion-colors outline-none hover:bg-hover",
                  "selected:bg-raised selected:shadow-[0_0_0_1px_var(--color-line)]",
                  "focus-visible:outline-2 focus-visible:outline-focus",
                )}
              >
                <Module step={step} height="h-4" />
                <span className="flex min-w-0 items-baseline gap-2">
                  <span
                    className={cx(
                      "type-numeral",
                      step.mark === "problem"
                        ? "text-fail-text"
                        : step.mark === "warning"
                          ? "text-attention-text"
                          : "text-ink",
                    )}
                  >
                    {String(index + 1).padStart(2, "0")}
                  </span>
                  {step.meta ? (
                    <span className="truncate type-small text-muted">{step.meta}</span>
                  ) : null}
                </span>
                {step.name ? (
                  <span className="line-clamp-2 type-step text-ink-2 group-selected:font-bold group-selected:text-ink">
                    {step.name}
                  </span>
                ) : null}
              </ListBoxItem>
            );
          }}
        </ListBox>
      </div>
    </div>
  );
}
