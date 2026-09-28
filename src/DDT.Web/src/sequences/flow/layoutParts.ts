// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequenceStep } from "../sequences";
import {
  ARROW_LENGTH,
  type FlowBoxKind,
  type FlowLayout,
  type Point,
  type Rect,
  type WireRoute,
} from "./flowGeometry";
import type { BodyName } from "./flowTree";

// The gap between the nodes of a series. At the top level, it's also above the first node and below the last.
export const GAP = 44;
// The height of an IF's curves to its branches and back to the join.
export const SPLIT = 56;
export const BRANCH_GAP = 40;
// A frame's padding at its sides and top, the room under its body for the slot at its end and the repeat's wire
// back, and the repeat's lane.
export const FRAME_PAD = 16;
export const FRAME_TAIL = 44;
export const LANE = 30;
// Where a repeat's wire back leaves the axis under the body, and joins it again under the header.
export const LOOP_LEAVES = 36;
export const LOOP_JOINS = 8;
// The slot at the end of a frame's body, between its last node and the repeat's wire back.
export const TAIL_SLOT = 18;
// An empty body or branch: a stretch of wire with its one slot.
export const EMPTY_WIDTH = 120;
export const EMPTY_HEIGHT = 56;

// A measured part: its size, where its axis runs from its left edge, and how to place it.
export interface Part {
  w: number;
  h: number;
  axis: number;
  id: string | null;
  // A frame draws the wire into itself, to its header card.
  framed: boolean;
  place: (x: number, y: number) => void;
}

export interface Place {
  parent: string | null;
  body: BodyName;
  depth: number;
  branch: WireRoute["branch"];
}

// What the parts of a layout draw into.
export interface Drawing {
  out: FlowLayout;
  line: (start: Point, end: Point, route: WireRoute) => void;
  slot: (x: number, y: number, parent: string | null, body: BodyName, index: number) => void;
  // Draws the wire from start down to to. It ends at the base of an arrowhead at the node arrowTo. Without arrowTo it
  // ends at to itself, where a frame or an empty series continues it. bend draws it as a curve. Returns where it ended.
  enter: (
    start: Point,
    to: Point,
    arrowTo: string | null,
    route: WireRoute,
    bend?: boolean,
  ) => Point;
  box: (node: SequenceStep, kind: FlowBoxKind, rect: Rect, extent: Rect, where: Place) => void;
}

// A drawing that also measures the nodes inside a part, which the layout supplies.
export interface LayoutContext extends Drawing {
  measure: (node: SequenceStep, where: Place) => Part;
  series: (list: readonly SequenceStep[], where: Place, bare?: boolean) => Part;
}

export function createDrawing(out: FlowLayout): Drawing {
  const line = (start: Point, end: Point, route: WireRoute) => {
    if (end.y > start.y) {
      out.wires.push({ shape: "line", start, end, ...route });
    }
  };

  return {
    out,
    line,
    slot: (x, y, parent, body, index) => {
      out.slots.push({ x, y, slot: { parent, body, index } });
    },
    enter: (start, to, arrowTo, route, bend = false) => {
      const end = { x: to.x, y: arrowTo === null ? to.y : to.y - 1 - ARROW_LENGTH };

      if (bend) {
        out.wires.push({ shape: "bend", start, end, ...route });
      } else {
        line(start, end, route);
      }

      if (arrowTo !== null) {
        out.arrows.push({ x: to.x, y: to.y - 1, to: arrowTo, branch: route.branch });
      }

      return end;
    },
    box: (node, kind, rect, extent, where) => {
      out.boxes.push({
        id: node.id,
        kind,
        ...rect,
        parent: where.parent,
        body: where.body,
        depth: where.depth,
        extent,
      });
    },
  };
}

// A part takes an arrowhead at its top unless it is a frame or empty.
export function arrowInto(part: Part): string | null {
  return part.framed ? null : part.id;
}
