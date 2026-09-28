// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequenceStep } from "../sequences";
import { HEADER_HEIGHT, LOOP_RADIUS, NODE_WIDTH } from "./flowGeometry";
import {
  arrowInto,
  FRAME_PAD,
  FRAME_TAIL,
  GAP,
  LANE,
  LOOP_JOINS,
  LOOP_LEAVES,
  TAIL_SLOT,
  type LayoutContext,
  type Part,
  type Place,
} from "./layoutParts";

type FramedStep = Extract<SequenceStep, { kind: "group" | "repeat" }>;

// A group or a repeat is a frame around its header card and its body; a repeat keeps a lane on the left for the
// wire back from the end of its body to its start.
export function framePart(
  context: LayoutContext,
  node: FramedStep,
  where: Place,
  inside: Place,
): Part {
  const kind = node.kind;
  const body = context.series(node.steps, inside);
  const lane = kind === "repeat" ? LANE : 0;
  const left = Math.max(NODE_WIDTH / 2, body.axis);
  const right = Math.max(NODE_WIDTH / 2, body.w - body.axis);
  const width = FRAME_PAD + lane + left + right + FRAME_PAD;
  const axis = FRAME_PAD + lane + left;
  const height = FRAME_PAD + HEADER_HEIGHT + GAP + body.h + FRAME_TAIL;

  return {
    w: width,
    h: height,
    axis,
    id: node.id,
    framed: true,
    place: (x, y) => {
      const axisX = x + axis;
      const header = {
        x: axisX - NODE_WIDTH / 2,
        y: y + FRAME_PAD,
        w: NODE_WIDTH,
        h: HEADER_HEIGHT,
      };
      const headerBottom = header.y + HEADER_HEIGHT;
      const bodyTop = headerBottom + GAP;
      const bodyBottom = bodyTop + body.h;
      const last = node.steps.at(-1)?.id ?? null;
      const extent = { x, y, w: width, h: height };

      context.out.frames.push({ id: node.id, kind, ...extent, depth: where.depth });
      context.enter({ x: axisX, y }, { x: axisX, y: header.y }, node.id, {
        from: null,
        to: node.id,
        branch: null,
      });
      context.box(node, kind, header, extent, where);
      context.enter({ x: axisX, y: headerBottom }, { x: axisX, y: bodyTop }, arrowInto(body), {
        from: node.id,
        to: body.id,
        branch: null,
      });
      body.place(axisX - body.axis, bodyTop);

      if (node.steps.length > 0) {
        context.slot(axisX, bodyTop - GAP / 2, node.id, "steps", 0);
        context.slot(axisX, bodyBottom + TAIL_SLOT, node.id, "steps", node.steps.length);
      }

      context.line(
        { x: axisX, y: bodyBottom },
        { x: axisX, y: y + height },
        {
          from: last ?? node.id,
          to: null,
          branch: null,
        },
      );

      if (kind === "repeat") {
        const laneX = x + FRAME_PAD / 2 + lane / 2;

        context.out.wires.push({
          shape: "loop",
          points: [
            { x: axisX, y: bodyBottom + LOOP_LEAVES },
            { x: laneX, y: bodyBottom + LOOP_LEAVES },
            { x: laneX, y: headerBottom + LOOP_JOINS },
            { x: axisX, y: headerBottom + LOOP_JOINS },
          ],
          radius: LOOP_RADIUS,
          from: node.id,
          to: node.id,
          branch: null,
        });
      }
    },
  };
}
