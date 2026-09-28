// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import type { CSSProperties, ReactNode } from "react";

import type { StepKind } from "@/sequences/sequences";

import { cx } from "./cx";
import { NodeGlyph } from "./NodeGlyph";
import { RailModule } from "./RailModule";
import type { RailStep, RailStepState } from "./SequenceRail";
import { SequenceRailStrip } from "./SequenceRailStrip";
import { StateTag } from "./StateTag";

// "edit" while a sequence is edited, where the module marks a problem; a run's state on a run's page.
export type FlowNodeState =
  "edit" | "waiting" | "running" | "done" | "failed" | "skipped" | "paused" | "notTaken";

const railStates: Record<FlowNodeState, RailStepState> = {
  edit: "waiting",
  waiting: "waiting",
  running: "running",
  done: "done",
  failed: "failed",
  skipped: "skipped",
  paused: "paused",
  notTaken: "waiting",
};

interface FlowNodeProps {
  kind: StepKind;
  name: string;
  // A leaf's number in the sequence, from 1; containers have none.
  number?: number | null;
  // One line under the name, such as the image a step applies, a test in words, or how long it took.
  detail?: ReactNode;
  // The detail is a value such as a template, in the monospaced face.
  code?: boolean;
  state?: FlowNodeState;
  // How far a running node is, from 0 to 100.
  percent?: number;
  selected?: boolean;
  // A finding of the node while it is edited: a problem keeps the sequence from running, a warning does not.
  mark?: "problem" | "warning";
  // The branch an IF took in a run, whose port label is then in ink.
  branch?: "then" | "else" | null;
  // A collapsed container's leaves, as the rail draws them, in place of its detail.
  strip?: RailStep[];
  collapsed?: boolean;
  className?: string;
  style?: CSSProperties;
}

// A node's card, with the rail's module along its top edge and, on an IF, the Then and Else ports on its bottom.
// A node the run did not take is drawn in outline with muted text rather than faded, so it keeps its contrast.
export function FlowNode({
  kind,
  name,
  number = null,
  detail,
  code = false,
  state = "edit",
  percent,
  selected = false,
  mark,
  branch = null,
  strip,
  collapsed = false,
  className,
  style,
}: FlowNodeProps) {
  const { t } = useLingui();
  const notTaken = state === "notTaken";
  const module: RailStep = {
    state: railStates[state],
    ...(percent === undefined ? {} : { percent }),
    ...(state === "edit" && mark !== undefined ? { mark } : {}),
  };
  const title =
    kind === "if"
      ? t`If: ${name}`
      : kind === "group"
        ? t`Group: ${name}`
        : kind === "repeat"
          ? t`Repeat: ${name}`
          : name;
  const ports = kind === "if" && !collapsed;
  const run = state !== "edit";

  return (
    <div
      data-flow-node
      className={cx(
        "flex flex-col overflow-hidden rounded-key motion-colors",
        notTaken
          ? "border-[1.5px] border-dashed border-line bg-transparent"
          : selected
            ? "bg-selected shadow-[0_0_0_2px_var(--color-focus)]"
            : "bg-raised shadow-[inset_0_0_0_1px_var(--color-line)]",
        className,
      )}
      style={style}
    >
      <RailModule
        step={module}
        className={cx("h-1.5 shrink-0 rounded-none shadow-none", notTaken && "bg-transparent")}
      />
      <div className="flex min-w-0 grow flex-col justify-center gap-1 px-3">
        <div className="flex min-w-0 items-center gap-2">
          <NodeGlyph kind={kind} className={notTaken ? "text-muted" : "text-ink-2"} />
          {number === null ? null : (
            <span className={cx("shrink-0 type-numeral", notTaken ? "text-muted" : "text-ink-2")}>
              {String(number).padStart(2, "0")}
            </span>
          )}
          <span
            className={cx("min-w-0 grow truncate type-label", notTaken ? "text-muted" : "text-ink")}
          >
            {title}
          </span>
          <NodeStateTag state={state} />
        </div>
        {strip !== undefined && collapsed ? (
          <SequenceRailStrip steps={strip} label={t`Steps of ${name}`} />
        ) : detail === undefined || detail === null ? null : (
          <div
            className={cx(
              "truncate",
              code ? "type-data" : "type-small",
              mark === "problem" && !run ? "text-fail-text" : "text-muted",
            )}
          >
            {detail}
          </div>
        )}
        {ports ? <IfPorts branch={branch} run={run} /> : null}
      </div>
    </div>
  );
}

function NodeStateTag({ state }: { state: FlowNodeState }) {
  const { t } = useLingui();

  return state === "running" ? (
    <StateTag tone="run" className="h-5">
      {t`Running`}
    </StateTag>
  ) : state === "paused" ? (
    <StateTag tone="attention" className="h-5">
      {t`Paused`}
    </StateTag>
  ) : state === "failed" ? (
    <StateTag tone="fail" className="h-5">
      {t`Failed`}
    </StateTag>
  ) : null;
}

function IfPorts({ branch, run }: { branch: "then" | "else" | null; run: boolean }) {
  const { t } = useLingui();

  return (
    <div className="flex justify-between type-label">
      <span className={branch === "then" ? "text-ink" : run ? "text-muted" : "text-ink-2"}>
        {t`Then`}
      </span>
      <span className={branch === "else" ? "text-ink" : run ? "text-muted" : "text-ink-2"}>
        {t`Else`}
      </span>
    </div>
  );
}
