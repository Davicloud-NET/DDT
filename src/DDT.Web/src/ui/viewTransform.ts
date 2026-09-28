// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// How a canvas shows its content: moved by x and y and scaled, so a point of the content is at
// content * scale + (x, y) on the screen, relative to the canvas's top left corner.
export interface ViewTransform {
  x: number;
  y: number;
  scale: number;
}

export interface ViewPoint {
  x: number;
  y: number;
}

export interface ViewSize {
  width: number;
  height: number;
}

export interface ViewRect {
  x: number;
  y: number;
  w: number;
  h: number;
}

export const MIN_SCALE = 0.25;
export const MAX_SCALE = 2;
// A zoom key or button changes the scale by this factor.
export const ZOOM_STEP = 1.25;
// Space around the content when it is fitted, and how much of it stays in view however far it is moved.
export const FIT_MARGIN = 32;
export const KEEP_IN_VIEW = 64;

export const IDENTITY: ViewTransform = { x: 0, y: 0, scale: 1 };

export function clampScale(scale: number): number {
  return Math.min(MAX_SCALE, Math.max(MIN_SCALE, scale));
}

export function toScreen(view: ViewTransform, point: ViewPoint): ViewPoint {
  return { x: point.x * view.scale + view.x, y: point.y * view.scale + view.y };
}

export function toContent(view: ViewTransform, point: ViewPoint): ViewPoint {
  return { x: (point.x - view.x) / view.scale, y: (point.y - view.y) / view.scale };
}

export function panBy(view: ViewTransform, dx: number, dy: number): ViewTransform {
  return { ...view, x: view.x + dx, y: view.y + dy };
}

// Scales to scale, keeping the content under anchor, a point on the screen, where it is.
export function zoomAt(view: ViewTransform, scale: number, anchor: ViewPoint): ViewTransform {
  const next = clampScale(scale);
  const fixed = toContent(view, anchor);

  return { scale: next, x: anchor.x - fixed.x * next, y: anchor.y - fixed.y * next };
}

export function zoomBy(view: ViewTransform, factor: number, anchor: ViewPoint): ViewTransform {
  return zoomAt(view, view.scale * factor, anchor);
}

// The whole content in the middle of the canvas, never larger than 100 %.
export function fitTransform(
  content: ViewSize,
  canvas: ViewSize,
  margin = FIT_MARGIN,
): ViewTransform {
  const room = {
    width: Math.max(1, canvas.width - margin * 2),
    height: Math.max(1, canvas.height - margin * 2),
  };
  const scale =
    content.width <= 0 || content.height <= 0
      ? 1
      : clampScale(Math.min(1, room.width / content.width, room.height / content.height));

  return {
    scale,
    x: (canvas.width - content.width * scale) / 2,
    y: (canvas.height - content.height * scale) / 2,
  };
}

// Moves the view back so at least KEEP_IN_VIEW pixels of the content, or all of it where it is smaller, stay on the
// canvas.
export function clampPan(view: ViewTransform, content: ViewSize, canvas: ViewSize): ViewTransform {
  const clampAxis = (offset: number, length: number, room: number) => {
    const keep = Math.min(KEEP_IN_VIEW, length);

    return Math.min(room - keep, Math.max(keep - length, offset));
  };

  return {
    scale: view.scale,
    x: clampAxis(view.x, content.width * view.scale, canvas.width),
    y: clampAxis(view.y, content.height * view.scale, canvas.height),
  };
}

// The part of the content the canvas shows, in the content's pixels.
export function visibleContent(view: ViewTransform, canvas: ViewSize): ViewRect {
  const topLeft = toContent(view, { x: 0, y: 0 });

  return { ...topLeft, w: canvas.width / view.scale, h: canvas.height / view.scale };
}

// Puts point of the content in the middle of the canvas.
export function centerOn(view: ViewTransform, point: ViewPoint, canvas: ViewSize): ViewTransform {
  return {
    scale: view.scale,
    x: canvas.width / 2 - point.x * view.scale,
    y: canvas.height / 2 - point.y * view.scale,
  };
}

// Moves the view as little as it takes to show rect of the content with margin around it, such as the node the
// keyboard went to.
export function ensureVisible(
  view: ViewTransform,
  rect: ViewRect,
  canvas: ViewSize,
  margin = FIT_MARGIN,
): ViewTransform {
  const axis = (offset: number, start: number, length: number, room: number) => {
    const from = start * view.scale + offset;
    const to = from + length * view.scale;

    if (length * view.scale + margin * 2 > room || from < margin) {
      return offset + margin - from;
    }

    return to > room - margin ? offset - (to - (room - margin)) : offset;
  };

  return {
    scale: view.scale,
    x: axis(view.x, rect.x, rect.w, canvas.width),
    y: axis(view.y, rect.y, rect.h, canvas.height),
  };
}

export interface WheelInput {
  deltaX: number;
  deltaY: number;
  // 0 pixels, 1 lines, 2 pages, as WheelEvent.deltaMode.
  deltaMode: number;
  ctrlKey: boolean;
  metaKey: boolean;
  shiftKey: boolean;
}

// A wheel moves the view; with Ctrl, which browsers also report for a pinch on a touchpad, it zooms at the pointer.
// Shift turns a wheel that only goes up and down sideways.
export function wheelChange(
  view: ViewTransform,
  wheel: WheelInput,
  anchor: ViewPoint,
  canvas: ViewSize,
): ViewTransform {
  const unit = wheel.deltaMode === 1 ? 16 : wheel.deltaMode === 2 ? canvas.height : 1;
  const dx = wheel.deltaX * unit;
  const dy = wheel.deltaY * unit;

  if (wheel.ctrlKey || wheel.metaKey) {
    const step = Math.max(-100, Math.min(100, dy));

    return zoomBy(view, 2 ** (-step / 200), anchor);
  }

  return wheel.shiftKey && dx === 0 ? panBy(view, -dy, 0) : panBy(view, -dx, -dy);
}

// Two fingers that moved from before to after: the view scales by how far apart they are now, around the point
// between them, and moves with that point.
export function pinchChange(
  view: ViewTransform,
  before: readonly [ViewPoint, ViewPoint],
  after: readonly [ViewPoint, ViewPoint],
): ViewTransform {
  const distance = ([a, b]: readonly [ViewPoint, ViewPoint]) => Math.hypot(b.x - a.x, b.y - a.y);
  const middle = ([a, b]: readonly [ViewPoint, ViewPoint]) => ({
    x: (a.x + b.x) / 2,
    y: (a.y + b.y) / 2,
  });
  const from = middle(before);
  const to = middle(after);
  const spread = distance(before);
  const scaled = spread > 0 ? zoomBy(view, distance(after) / spread, from) : view;

  return panBy(scaled, to.x - from.x, to.y - from.y);
}

export type ZoomCommand = "in" | "out" | "actual" | "fit";

// + and - zoom, 0 shows 100 % and 1 fits the content, without Ctrl, which zooms the browser's page.
export function zoomKey(event: {
  key: string;
  ctrlKey: boolean;
  metaKey: boolean;
  altKey: boolean;
}): ZoomCommand | null {
  if (event.ctrlKey || event.metaKey || event.altKey) {
    return null;
  }

  switch (event.key) {
    case "+":
    case "=":
      return "in";
    case "-":
    case "_":
      return "out";
    case "0":
      return "actual";
    case "1":
      return "fit";
    default:
      return null;
  }
}
