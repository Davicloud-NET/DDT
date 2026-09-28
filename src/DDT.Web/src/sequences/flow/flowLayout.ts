// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequenceStep } from "../sequences";
import type { BodyName, Slot } from "./flowTree";

// Where the flow draws a sequence, worked out from the tree alone: nothing is placed by hand and nothing is saved.
// The flow runs top to bottom. A series stacks its nodes on one axis with a gap between them. An IF is its card with
// the Then and Else ports on its bottom edge, a curve from each port to its branch, the branches side by side, and
// curves from both back to a join dot on the axis. A group or a repeat is a frame around its header card and its
// body; a repeat keeps a lane on the left for the wire back from the end of its body to its start. The same tree
// always gives the same layout, and the numbers are pixels of the flow at 100 %.

export const NODE_WIDTH = 236;
export const LEAF_HEIGHT = 64;
export const IF_HEIGHT = 100;
// A group's or a repeat's header card, and a container shown collapsed.
export const HEADER_HEIGHT = 84;
// The Then and Else ports sit this far in from the sides of an IF's card.
export const PORT_INSET = 25;
// An arrowhead ends 1 px before the node it points at; its wire ends at its base.
export const ARROW_LENGTH = 8;
export const ARROW_HALF_WIDTH = 5;
export const LOOP_RADIUS = 12;

// Between the nodes of a series; above the first and below the last node at the top.
const GAP = 44;
// The height of an IF's curves to its branches and back to the join.
const SPLIT = 56;
const BRANCH_GAP = 40;
// A frame's padding at its sides and top, the room under its body for the slot at its end and the repeat's wire
// back, and the repeat's lane.
const FRAME_PAD = 16;
const FRAME_TAIL = 44;
const LANE = 30;
// Where a repeat's wire back leaves the axis under the body, and joins it again under the header.
const LOOP_LEAVES = 36;
const LOOP_JOINS = 8;
// The slot at the end of a frame's body, between its last node and the repeat's wire back.
const TAIL_SLOT = 18;
// An empty body or branch: a stretch of wire with its one slot.
const EMPTY_WIDTH = 120;
const EMPTY_HEIGHT = 56;

export interface Point {
  x: number;
  y: number;
}

export interface Rect {
  x: number;
  y: number;
  w: number;
  h: number;
}

export type FlowBoxKind = "leaf" | "if" | "group" | "repeat" | "collapsed";

// A node's card. extent is all the node takes: the card for a leaf, the frame for a group or a repeat, the card,
// the branches and the join for an IF.
export interface FlowBox extends Rect {
  id: string;
  kind: FlowBoxKind;
  parent: string | null;
  body: BodyName;
  depth: number;
  extent: Rect;
}

export interface FlowFrame extends Rect {
  id: string;
  kind: "group" | "repeat";
  depth: number;
}

export interface FlowPort extends Point {
  id: string;
  branch: "then" | "else";
}

// Where an IF's branches meet again, on the axis at the bottom of its extent.
export interface FlowJoin extends Point {
  id: string;
}

// What a wire connects, so a run can draw the path it took: from the node before it to the node after it, null
// where it starts or ends at a port, a join or a frame's edge. branch is set on the wires of an IF's branch that no
// node of the branch decides: the curves, the straight run to the join, and an empty branch's wire.
export interface WireRoute {
  from: string | null;
  to: string | null;
  branch: { id: string; name: "then" | "else" } | null;
}

// A line; a curve that leaves start and reaches end going down, as a cubic whose control points sit halfway down
// above each end; or a repeat's wire back, a polyline with rounded corners.
export type FlowWire =
  | ({ shape: "line"; start: Point; end: Point } & WireRoute)
  | ({ shape: "bend"; start: Point; end: Point } & WireRoute)
  | ({ shape: "loop"; points: Point[]; radius: number } & WireRoute);

// An arrowhead pointing down, by its tip.
export interface FlowArrow extends Point {
  to: string;
  branch: WireRoute["branch"];
}

// A gap on a wire where nodes can go, by its middle.
export interface FlowSlot extends Point {
  slot: Slot;
}

export interface FlowLayout {
  width: number;
  height: number;
  boxes: FlowBox[];
  frames: FlowFrame[];
  ports: FlowPort[];
  joins: FlowJoin[];
  wires: FlowWire[];
  arrows: FlowArrow[];
  slots: FlowSlot[];
}

export interface FlowLayoutOptions {
  // Containers drawn as one card, without what is inside them.
  collapsed?: ReadonlySet<string>;
}

// A measured part: its size, where its axis runs from its left edge, and how to place it.
interface Part {
  w: number;
  h: number;
  axis: number;
  id: string | null;
  // A frame takes the wire into it itself, to its header card.
  framed: boolean;
  place: (x: number, y: number) => void;
}

interface Place {
  parent: string | null;
  body: BodyName;
  depth: number;
  branch: WireRoute["branch"];
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

  const line = (start: Point, end: Point, route: WireRoute) => {
    if (end.y > start.y) {
      out.wires.push({ shape: "line", start, end, ...route });
    }
  };

  const slot = (x: number, y: number, parent: string | null, body: BodyName, index: number) => {
    out.slots.push({ x, y, slot: { parent, body, index } });
  };

  // The wire from start down to y: to the base of an arrowhead at the node arrowTo, or, without one, to y itself,
  // where a frame or an empty series carries it on. bend draws it as a curve. Answers where the wire ended.
  const enter = (
    start: Point,
    x: number,
    y: number,
    arrowTo: string | null,
    route: WireRoute,
    bend = false,
  ): Point => {
    const end = { x, y: arrowTo === null ? y : y - 1 - ARROW_LENGTH };

    if (bend) {
      out.wires.push({ shape: "bend", start, end, ...route });
    } else {
      line(start, end, route);
    }

    if (arrowTo !== null) {
      out.arrows.push({ x, y: y - 1, to: arrowTo, branch: route.branch });
    }

    return end;
  };

  // A part takes an arrowhead at its top unless it is a frame or empty.
  const arrowInto = (part: Part) => (part.framed ? null : part.id);

  const box = (node: SequenceStep, kind: FlowBoxKind, rect: Rect, extent: Rect, where: Place) => {
    out.boxes.push({
      id: node.id,
      kind,
      ...rect,
      parent: where.parent,
      body: where.body,
      depth: where.depth,
      extent,
    });
  };

  const series = (list: readonly SequenceStep[], where: Place, bare = false): Part => {
    const kids = list.map((node) => measure(node, where));

    if (kids.length === 0) {
      return {
        w: EMPTY_WIDTH,
        h: EMPTY_HEIGHT,
        axis: EMPTY_WIDTH / 2,
        id: null,
        framed: false,
        place: (x, y) => {
          const axis = x + EMPTY_WIDTH / 2;

          if (!bare) {
            line(
              { x: axis, y },
              { x: axis, y: y + EMPTY_HEIGHT },
              {
                from: null,
                to: null,
                branch: where.branch,
              },
            );
          }

          slot(axis, y + EMPTY_HEIGHT / 2, where.parent, where.body, 0);
        },
      };
    }

    const axis = Math.max(...kids.map((kid) => kid.axis));
    const right = Math.max(...kids.map((kid) => kid.w - kid.axis));
    const height = kids.reduce((sum, kid) => sum + kid.h, 0) + GAP * (kids.length - 1);

    return {
      w: axis + right,
      h: height,
      axis,
      id: kids[0]?.id ?? null,
      framed: kids[0]?.framed ?? false,
      place: (x, y) => {
        let top = y;
        let previous: Part | null = null;

        kids.forEach((kid, index) => {
          if (previous !== null) {
            enter({ x: x + axis, y: top - GAP }, x + axis, top, arrowInto(kid), {
              from: previous.id,
              to: kid.id,
              branch: null,
            });
            slot(x + axis, top - GAP / 2, where.parent, where.body, index);
          }

          kid.place(x + axis - kid.axis, top);
          top += kid.h + GAP;
          previous = kid;
        });
      },
    };
  };

  const measure = (node: SequenceStep, where: Place): Part => {
    const inside: Place = { parent: node.id, body: "steps", depth: where.depth + 1, branch: null };

    if (
      collapsed.has(node.id) &&
      (node.kind === "group" || node.kind === "if" || node.kind === "repeat")
    ) {
      return card(node, "collapsed", HEADER_HEIGHT, where);
    }

    switch (node.kind) {
      case "if":
        return branches(node, where, inside);
      case "group":
      case "repeat":
        return frame(node, node.kind, where, inside);
      default:
        return card(node, "leaf", LEAF_HEIGHT, where);
    }
  };

  const card = (node: SequenceStep, kind: FlowBoxKind, height: number, where: Place): Part => ({
    w: NODE_WIDTH,
    h: height,
    axis: NODE_WIDTH / 2,
    id: node.id,
    framed: false,
    place: (x, y) => {
      const rect = { x, y, w: NODE_WIDTH, h: height };

      box(node, kind, rect, rect, where);
    },
  });

  const branches = (
    node: Extract<SequenceStep, { kind: "if" }>,
    where: Place,
    inside: Place,
  ): Part => {
    const routeOf = (name: "then" | "else") => ({ id: node.id, name });
    const then = series(node.then, { ...inside, body: "then", branch: routeOf("then") });
    const otherwise = series(node.else, { ...inside, body: "else", branch: routeOf("else") });
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
        const join = { x: x + axis, y: joinStart + SPLIT };
        const rect = { x: cardLeft, y, w: NODE_WIDTH, h: IF_HEIGHT };

        box(node, "if", rect, { x, y, w: width, h: height }, where);

        const sides = [
          {
            name: "then" as const,
            part: then,
            list: node.then,
            left: x + offset,
            port: cardLeft + PORT_INSET,
          },
          {
            name: "else" as const,
            part: otherwise,
            list: node.else,
            left: x + offset + then.w + BRANCH_GAP,
            port: cardLeft + NODE_WIDTH - PORT_INSET,
          },
        ];

        for (const side of sides) {
          const route = routeOf(side.name);
          const port = { x: side.port, y: portsY };
          const branchAxis = side.left + side.part.axis;
          const bottom = branchTop + side.part.h;
          const last = side.list.at(-1)?.id ?? null;

          out.ports.push({ id: node.id, branch: side.name, ...port });

          const entered = enter(
            port,
            branchAxis,
            branchTop,
            arrowInto(side.part),
            { from: node.id, to: side.part.id, branch: route },
            true,
          );

          side.part.place(side.left, branchTop);

          // The first slot sits halfway along the curve from the port; the last under the last node where the
          // branch is shorter than the other, and halfway along the curve to the join otherwise.
          if (side.list.length > 0) {
            const count = side.list.length;
            const roomy = joinStart - bottom >= GAP;

            slot((port.x + entered.x) / 2, (port.y + entered.y) / 2, node.id, side.name, 0);
            slot(
              roomy ? branchAxis : (branchAxis + join.x) / 2,
              roomy ? bottom + GAP / 2 : joinStart + SPLIT / 2,
              node.id,
              side.name,
              count,
            );
          }

          line(
            { x: branchAxis, y: bottom },
            { x: branchAxis, y: joinStart },
            {
              from: last,
              to: null,
              branch: route,
            },
          );
          out.wires.push({
            shape: "bend",
            start: { x: branchAxis, y: joinStart },
            end: join,
            from: last,
            to: null,
            branch: route,
          });
        }

        out.joins.push({ id: node.id, ...join });
      },
    };
  };

  const frame = (
    node: Extract<SequenceStep, { kind: "group" | "repeat" }>,
    kind: "group" | "repeat",
    where: Place,
    inside: Place,
  ): Part => {
    const body = series(node.steps, inside);
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

        out.frames.push({ id: node.id, kind, ...extent, depth: where.depth });
        enter({ x: axisX, y }, axisX, header.y, node.id, { from: null, to: node.id, branch: null });
        box(node, kind, header, extent, where);
        enter({ x: axisX, y: headerBottom }, axisX, bodyTop, arrowInto(body), {
          from: node.id,
          to: body.id,
          branch: null,
        });
        body.place(axisX - body.axis, bodyTop);

        if (node.steps.length > 0) {
          slot(axisX, bodyTop - GAP / 2, node.id, "steps", 0);
          slot(axisX, bodyBottom + TAIL_SLOT, node.id, "steps", node.steps.length);
        }

        line(
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

          out.wires.push({
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
  };

  const top = series(steps, { parent: null, body: "steps", depth: 0, branch: null }, true);

  top.place(0, GAP);

  if (steps.length > 0) {
    slot(top.axis, GAP / 2, null, "steps", 0);
    slot(top.axis, GAP + top.h + GAP / 2, null, "steps", steps.length);
  }

  out.width = top.w;
  out.height = top.h + GAP * 2;

  return out;
}

const round = (value: number) => String(Math.round(value * 10) / 10);

// The SVG path of a wire.
export function wirePath(wire: FlowWire): string {
  switch (wire.shape) {
    case "line":
      return `M${round(wire.start.x)} ${round(wire.start.y)} L${round(wire.end.x)} ${round(wire.end.y)}`;
    case "bend": {
      const middle = (wire.start.y + wire.end.y) / 2;

      return (
        `M${round(wire.start.x)} ${round(wire.start.y)} ` +
        `C${round(wire.start.x)} ${round(middle)} ${round(wire.end.x)} ${round(middle)} ` +
        `${round(wire.end.x)} ${round(wire.end.y)}`
      );
    }
    case "loop":
      return roundedPath(wire.points, wire.radius);
  }
}

// A polyline whose corners are quarter curves of the radius, or less where a leg is short.
function roundedPath(points: readonly Point[], radius: number): string {
  const [first] = points;
  const final = points.at(-1);

  if (first === undefined || final === undefined) {
    return "";
  }

  let path = `M${round(first.x)} ${round(first.y)}`;

  for (let index = 1; index < points.length - 1; index++) {
    const before = points[index - 1] ?? first;
    const corner = points[index] ?? first;
    const after = points[index + 1] ?? final;
    const into = Math.hypot(corner.x - before.x, corner.y - before.y);
    const out = Math.hypot(after.x - corner.x, after.y - corner.y);
    const r = Math.min(radius, into / 2, out / 2);
    const a = {
      x: corner.x - ((corner.x - before.x) / into) * r,
      y: corner.y - ((corner.y - before.y) / into) * r,
    };
    const b = {
      x: corner.x + ((after.x - corner.x) / out) * r,
      y: corner.y + ((after.y - corner.y) / out) * r,
    };

    path += ` L${round(a.x)} ${round(a.y)} Q${round(corner.x)} ${round(corner.y)} ${round(b.x)} ${round(b.y)}`;
  }

  return `${path} L${round(final.x)} ${round(final.y)}`;
}

export function arrowPath(arrow: FlowArrow): string {
  const base = arrow.y - ARROW_LENGTH;

  return (
    `M${round(arrow.x)} ${round(arrow.y)} ` +
    `L${round(arrow.x - ARROW_HALF_WIDTH)} ${round(base)} ` +
    `L${round(arrow.x + ARROW_HALF_WIDTH)} ${round(base)} Z`
  );
}
