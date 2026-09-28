// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequenceStep } from "../sequences";
import { IF_HEIGHT, NODE_WIDTH, PORT_INSET, type Point } from "./flowGeometry";
import {
  arrowInto,
  BRANCH_GAP,
  GAP,
  SPLIT,
  type LayoutContext,
  type Part,
  type Place,
} from "./layoutParts";

type IfStep = Extract<SequenceStep, { kind: "if" }>;

// One branch of an IF as it is placed: its part, its nodes, its left edge and its port on the card.
interface Side {
  name: "then" | "else";
  part: Part;
  list: readonly SequenceStep[];
  left: number;
  port: number;
}

// Where an IF's branches leave its ports, start, end and meet again at the join.
interface Junction {
  id: string;
  portsY: number;
  branchTop: number;
  joinStart: number;
  join: Point;
}

// An IF is its card with the Then and Else ports on its bottom edge, the branches side by side under it, and curves
// from each port to its branch and back to a join dot on the axis.
export function ifPart(context: LayoutContext, node: IfStep, where: Place, inside: Place): Part {
  const routeOf = (name: "then" | "else") => ({ id: node.id, name });
  const then = context.series(node.then, { ...inside, body: "then", branch: routeOf("then") });
  const otherwise = context.series(node.else, { ...inside, body: "else", branch: routeOf("else") });
  const wide = then.w + BRANCH_GAP + otherwise.w;
  const width = Math.max(NODE_WIDTH, wide);
  const axis = width / 2;
  const offset = (width - wide) / 2;
  const tall = Math.max(then.h, otherwise.h);
  const height = IF_HEIGHT + SPLIT + tall + SPLIT;

  return {
    w: width,
    h: height,
    axis,
    id: node.id,
    framed: false,
    place: (x, y) => {
      const cardLeft = x + axis - NODE_WIDTH / 2;
      const portsY = y + IF_HEIGHT;
      const branchTop = portsY + SPLIT;
      const joinStart = branchTop + tall;
      const junction = {
        id: node.id,
        portsY,
        branchTop,
        joinStart,
        join: { x: x + axis, y: joinStart + SPLIT },
      };
      const rect = { x: cardLeft, y, w: NODE_WIDTH, h: IF_HEIGHT };

      context.box(node, "if", rect, { x, y, w: width, h: height }, where);
      placeSide(context, junction, {
        name: "then",
        part: then,
        list: node.then,
        left: x + offset,
        port: cardLeft + PORT_INSET,
      });
      placeSide(context, junction, {
        name: "else",
        part: otherwise,
        list: node.else,
        left: x + offset + then.w + BRANCH_GAP,
        port: cardLeft + NODE_WIDTH - PORT_INSET,
      });
      context.out.joins.push({ id: node.id, ...junction.join });
    },
  };
}

function placeSide(context: LayoutContext, junction: Junction, side: Side): void {
  const route = { id: junction.id, name: side.name };
  const port = { x: side.port, y: junction.portsY };
  const branchAxis = side.left + side.part.axis;
  const bottom = junction.branchTop + side.part.h;
  const last = side.list.at(-1)?.id ?? null;

  context.out.ports.push({ id: junction.id, branch: side.name, ...port });

  const entered = context.enter(
    port,
    { x: branchAxis, y: junction.branchTop },
    arrowInto(side.part),
    { from: junction.id, to: side.part.id, branch: route },
    true,
  );

  side.part.place(side.left, junction.branchTop);

  // The first slot sits halfway along the curve from the port; the last under the last node where the
  // branch is shorter than the other, and halfway along the curve to the join otherwise.
  if (side.list.length > 0) {
    const count = side.list.length;
    const roomy = junction.joinStart - bottom >= GAP;

    context.slot((port.x + entered.x) / 2, (port.y + entered.y) / 2, junction.id, side.name, 0);
    context.slot(
      roomy ? branchAxis : (branchAxis + junction.join.x) / 2,
      roomy ? bottom + GAP / 2 : junction.joinStart + SPLIT / 2,
      junction.id,
      side.name,
      count,
    );
  }

  context.line(
    { x: branchAxis, y: bottom },
    { x: branchAxis, y: junction.joinStart },
    {
      from: last,
      to: null,
      branch: route,
    },
  );
  context.out.wires.push({
    shape: "bend",
    start: { x: branchAxis, y: junction.joinStart },
    end: junction.join,
    from: last,
    to: null,
    branch: route,
  });
}
