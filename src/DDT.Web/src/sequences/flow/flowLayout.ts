// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequenceStep } from "../sequences";
import {
  HEADER_HEIGHT,
  LEAF_HEIGHT,
  NODE_WIDTH,
  type FlowBoxKind,
  type FlowLayout,
} from "./flowGeometry";
import { framePart } from "./layoutFrame";
import { ifPart } from "./layoutBranches";
import { createDrawing, GAP, type LayoutContext, type Part, type Place } from "./layoutParts";
import { seriesPart } from "./layoutSeries";

// Where the flow draws a sequence, top to bottom, from the tree alone: the same tree always gives the same layout,
// and nothing is placed by hand or saved. The numbers are pixels of the flow at 100 %.

export interface FlowLayoutOptions {
  // Containers drawn as one card, without what is inside them.
  collapsed?: ReadonlySet<string>;
}

export function layoutFlow(
  steps: readonly SequenceStep[],
  options: FlowLayoutOptions = {},
): FlowLayout {
  const collapsed = options.collapsed ?? new Set<string>();
  const out: FlowLayout = {
    width: 0,
    height: 0,
    boxes: [],
    frames: [],
    ports: [],
    joins: [],
    wires: [],
    arrows: [],
    slots: [],
  };
  const drawing = createDrawing(out);
  const context: LayoutContext = {
    ...drawing,
    measure: (node, where) => measure(context, collapsed, node, where),
    series: (list, where, bare = false) => seriesPart(context, list, where, bare),
  };

  const top = context.series(steps, { parent: null, body: "steps", depth: 0, branch: null }, true);

  top.place(0, GAP);

  if (steps.length > 0) {
    drawing.slot(top.axis, GAP / 2, null, "steps", 0);
    drawing.slot(top.axis, GAP + top.h + GAP / 2, null, "steps", steps.length);
  }

  out.width = top.w;
  out.height = top.h + GAP * 2;

  return out;
}

function measure(
  context: LayoutContext,
  collapsed: ReadonlySet<string>,
  node: SequenceStep,
  where: Place,
): Part {
  const inside: Place = { parent: node.id, body: "steps", depth: where.depth + 1, branch: null };

  if (
    collapsed.has(node.id) &&
    (node.kind === "group" || node.kind === "if" || node.kind === "repeat")
  ) {
    return cardPart(context, node, "collapsed", HEADER_HEIGHT, where);
  }

  switch (node.kind) {
    case "if":
      return ifPart(context, node, where, inside);
    case "group":
    case "repeat":
      return framePart(context, node, where, inside);
    default:
      return cardPart(context, node, "leaf", LEAF_HEIGHT, where);
  }
}

function cardPart(
  context: LayoutContext,
  node: SequenceStep,
  kind: FlowBoxKind,
  height: number,
  where: Place,
): Part {
  return {
    w: NODE_WIDTH,
    h: height,
    axis: NODE_WIDTH / 2,
    id: node.id,
    framed: false,
    place: (x, y) => {
      const rect = { x, y, w: NODE_WIDTH, h: height };

      context.box(node, kind, rect, rect, where);
    },
  };
}
