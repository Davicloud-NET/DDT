// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import {
  ARROW_HALF_WIDTH,
  ARROW_LENGTH,
  type FlowArrow,
  type FlowWire,
  type Point,
} from "./flowGeometry";

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
