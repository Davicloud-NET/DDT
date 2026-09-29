// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useRef, type PointerEvent, type RefObject } from "react";

import { panBy, pinchChange, type ViewPoint, type ViewTransform } from "./viewTransform";

// Where a drag or a pinch started, so each move is worked out from there rather than from the last render.
interface Gesture {
  view: ViewTransform;
  points: Map<number, ViewPoint>;
  start: Map<number, ViewPoint>;
}

// What a press on the canvas leaves alone: controls, and the nodes, which the flow drags itself.
const notPanned =
  'button, a, input, textarea, select, [role="button"], [data-flow-node], [data-no-pan]';

function pointAt(event: PointerEvent): ViewPoint {
  const bounds = event.currentTarget.getBoundingClientRect();

  return { x: event.clientX - bounds.left, y: event.clientY - bounds.top };
}

// Drags the canvas with one pointer on its background and pinches it with two.
export function usePointerGestures(
  latest: RefObject<ViewTransform>,
  change: (next: ViewTransform) => void,
) {
  const gesture = useRef<Gesture | null>(null);

  const onPointerDown = (event: PointerEvent<HTMLDivElement>) => {
    const target = event.target as Element;

    if (
      event.button > 1 ||
      (target !== event.currentTarget && target.closest(notPanned) !== null)
    ) {
      return;
    }

    const point = pointAt(event);
    const points = new Map(gesture.current?.points ?? []);

    points.set(event.pointerId, point);
    event.currentTarget.setPointerCapture(event.pointerId);
    // A second finger restarts the gesture as a pinch from where both fingers are.
    gesture.current = { view: latest.current, points, start: new Map(points) };
  };

  const onPointerMove = (event: PointerEvent<HTMLDivElement>) => {
    const current = gesture.current;

    if (!current?.points.has(event.pointerId)) {
      return;
    }

    current.points.set(event.pointerId, pointAt(event));

    const ids = [...current.start.keys()].slice(0, 2);
    const [first, second] = ids.map((id) => ({
      from: current.start.get(id) ?? { x: 0, y: 0 },
      to: current.points.get(id) ?? { x: 0, y: 0 },
    }));

    if (first !== undefined && second !== undefined) {
      change(pinchChange(current.view, [first.from, second.from], [first.to, second.to]));
    } else if (first !== undefined) {
      change(panBy(current.view, first.to.x - first.from.x, first.to.y - first.from.y));
    }
  };

  const onPointerEnd = (event: PointerEvent<HTMLDivElement>) => {
    const current = gesture.current;

    if (current === null) {
      return;
    }

    current.points.delete(event.pointerId);
    gesture.current =
      current.points.size === 0
        ? null
        : { view: latest.current, points: current.points, start: new Map(current.points) };
  };

  return {
    onPointerDown,
    onPointerMove,
    onPointerUp: onPointerEnd,
    onPointerCancel: onPointerEnd,
  };
}
