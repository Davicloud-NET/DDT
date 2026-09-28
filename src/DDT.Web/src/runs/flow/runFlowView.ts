// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { FlowLayout } from "@/sequences/flow/flowGeometry";
import type { FlowNodeState } from "@/ui/FlowNode";
import type { MinimapItem } from "@/ui/Minimap";

import type { PathNode, PathState, RunPath } from "../runPath";

export const flowState: Record<PathState, FlowNodeState> = {
  waiting: "waiting",
  running: "running",
  paused: "paused",
  done: "done",
  failed: "failed",
  skipped: "skipped",
  notTaken: "notTaken",
};

export function branchOf(node: PathNode): "then" | "else" | null {
  const branch = node.step?.branch ?? null;

  return node.node.kind !== "if" || node.state === "notTaken"
    ? null
    : branch === "Then"
      ? "then"
      : branch === "Else"
        ? "else"
        : null;
}

// The first node at the top that the run reached in the installed Windows after nodes in Windows PE, where the flow
// draws the line between the phases; null where the run has not handed over, or the line would not run across.
export function handover(path: RunPath): string | null {
  const top = path.nodes.filter((node) => node.entry.parent === null);
  const index = top.findIndex((node) => node.step?.phase === "Windows");

  return index > 0 && top.slice(0, index).every((node) => node.step?.phase !== "Windows")
    ? (top[index]?.node.id ?? null)
    : null;
}

export function minimapItems(layout: FlowLayout, path: RunPath): MinimapItem[] {
  return [
    ...layout.frames.map((frame) => ({ ...frame, tone: "frame" as const })),
    ...layout.boxes.map((box) => ({
      ...box,
      tone: path.byId.get(box.id)?.state === "notTaken" ? ("muted" as const) : ("node" as const),
    })),
  ];
}
