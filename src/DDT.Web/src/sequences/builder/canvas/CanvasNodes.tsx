// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { KeyboardEvent } from "react";

import type { ViewRect } from "@/ui/viewTransform";

import type { FlowBox } from "../../flow/flowGeometry";
import { nodeLabel } from "../../flow/flowLabels";
import type { TreeIndex } from "../../flow/flowTree";
import { stepFindings, type Findings } from "../../problems";
import type { SequenceStep } from "../../sequences";
import type { FlowDrag } from "../flowDrag";
import type { NodeDetail } from "../nodeDetail";
import { CanvasNode } from "./CanvasNode";
import { collapsedStrip, findingMark } from "./nodeMarks";

interface CanvasNodesProps {
  boxes: readonly FlowBox[];
  index: TreeIndex;
  findings: Findings;
  selectedId: string | null;
  tabbableId: string | null;
  locked: boolean;
  detailOf: (node: SequenceStep) => NodeDetail;
  register: (id: string) => (element: HTMLElement | null) => void;
  onSelect: (id: string) => void;
  onReveal: (rect: ViewRect) => void;
  onKeyDown: (event: KeyboardEvent, id: string) => void;
  onMenu: (id: string, element: HTMLElement) => void;
  onDragChange: (drag: FlowDrag) => void;
}

// The canvas's cards, each placed at its box in the layout.
export function CanvasNodes({
  boxes,
  index,
  findings,
  selectedId,
  tabbableId,
  locked,
  detailOf,
  register,
  onSelect,
  onReveal,
  onKeyDown,
  onMenu,
  onDragChange,
}: CanvasNodesProps) {
  return boxes.map((box) => {
    const entry = index.byId.get(box.id);

    if (entry === undefined) {
      return null;
    }

    const own = stepFindings(findings, box.id);

    return (
      <CanvasNode
        key={box.id}
        node={entry.node}
        box={box}
        number={entry.number}
        label={nodeLabel(index, box.id, own.problems.length, own.warnings.length)}
        detail={detailOf(entry.node)}
        mark={findingMark(own)}
        collapsed={box.kind === "collapsed"}
        strip={box.kind === "collapsed" ? collapsedStrip(index, findings, box.id) : undefined}
        selected={box.id === selectedId}
        tabbable={box.id === tabbableId}
        locked={locked}
        register={register(box.id)}
        onPress={() => {
          onSelect(box.id);
        }}
        onFocus={() => {
          onReveal(box);
        }}
        onKeyDown={(event) => {
          onKeyDown(event, box.id);
        }}
        onContextMenu={(element) => {
          onSelect(box.id);
          onMenu(box.id, element);
        }}
        onDragChange={onDragChange}
      />
    );
  });
}
