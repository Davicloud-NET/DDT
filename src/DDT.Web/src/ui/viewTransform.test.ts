// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// @vitest-environment node

import { describe, expect, it } from "vitest";

import {
  centerOn,
  clampPan,
  ensureVisible,
  fitTransform,
  IDENTITY,
  KEEP_IN_VIEW,
  MAX_SCALE,
  MIN_SCALE,
  panBy,
  pinchChange,
  toContent,
  toScreen,
  visibleContent,
  wheelChange,
  zoomAt,
  zoomKey,
  type ViewTransform,
} from "./viewTransform";

const canvas = { width: 800, height: 600 };

function close(actual: ViewTransform, expected: ViewTransform) {
  expect(actual.x).toBeCloseTo(expected.x, 6);
  expect(actual.y).toBeCloseTo(expected.y, 6);
  expect(actual.scale).toBeCloseTo(expected.scale, 6);
}

describe("the view transform", () => {
  it("turns content points into screen points and back", () => {
    const view = { x: 40, y: -20, scale: 0.5 };
    const point = { x: 100, y: 300 };

    expect(toScreen(view, point)).toEqual({ x: 90, y: 130 });
    expect(toContent(view, toScreen(view, point))).toEqual(point);
  });

  it("zooms around the point under the pointer, within its bounds", () => {
    const view = { x: 10, y: 20, scale: 1 };
    const anchor = { x: 300, y: 200 };
    const zoomed = zoomAt(view, 1.5, anchor);

    expect(toScreen(zoomed, toContent(view, anchor))).toEqual(anchor);
    expect(zoomAt(view, 10, anchor).scale).toBe(MAX_SCALE);
    expect(zoomAt(view, 0.01, anchor).scale).toBe(MIN_SCALE);
  });

  it("fits the content in the middle, never above 100 %", () => {
    close(fitTransform({ width: 1_472, height: 2_000 }, canvas), {
      scale: (600 - 64) / 2_000,
      x: (800 - 1_472 * ((600 - 64) / 2_000)) / 2,
      y: 32,
    });
    close(fitTransform({ width: 200, height: 100 }, canvas), { scale: 1, x: 300, y: 250 });
    expect(fitTransform({ width: 0, height: 0 }, canvas).scale).toBe(1);
  });

  it("keeps some of the content in view however far it is moved", () => {
    const content = { width: 1_000, height: 400 };
    const far = clampPan(panBy(IDENTITY, 5_000, -5_000), content, canvas);

    expect(far.x).toBe(canvas.width - KEEP_IN_VIEW);
    expect(far.y).toBe(KEEP_IN_VIEW - content.height);
    expect(clampPan({ x: -100, y: 50, scale: 1 }, content, canvas)).toEqual({
      x: -100,
      y: 50,
      scale: 1,
    });
  });

  it("says what part of the content shows, and centres a point of it", () => {
    const view = { x: -200, y: -100, scale: 2 };

    expect(visibleContent(view, canvas)).toEqual({ x: 100, y: 50, w: 400, h: 300 });
    expect(toScreen(centerOn(view, { x: 500, y: 500 }, canvas), { x: 500, y: 500 })).toEqual({
      x: 400,
      y: 300,
    });
  });

  it("moves as little as it takes to show a node", () => {
    const view = { x: 0, y: 0, scale: 1 };

    expect(ensureVisible(view, { x: 100, y: 100, w: 236, h: 64 }, canvas)).toEqual(view);
    expect(ensureVisible(view, { x: 100, y: 900, w: 236, h: 64 }, canvas)).toEqual({
      x: 0,
      y: 600 - 32 - 964,
      scale: 1,
    });
    expect(ensureVisible(view, { x: -300, y: 100, w: 236, h: 64 }, canvas)).toEqual({
      x: 332,
      y: 0,
      scale: 1,
    });
  });
});

describe("input", () => {
  const wheel = {
    deltaX: 0,
    deltaY: 0,
    deltaMode: 0,
    ctrlKey: false,
    metaKey: false,
    shiftKey: false,
  };

  it("moves with the wheel, sideways with Shift, in lines and pages too", () => {
    const anchor = { x: 0, y: 0 };

    expect(wheelChange(IDENTITY, { ...wheel, deltaY: 30, deltaX: 5 }, anchor, canvas)).toEqual({
      x: -5,
      y: -30,
      scale: 1,
    });
    expect(wheelChange(IDENTITY, { ...wheel, deltaY: 30, shiftKey: true }, anchor, canvas)).toEqual(
      { x: -30, y: 0, scale: 1 },
    );
    expect(wheelChange(IDENTITY, { ...wheel, deltaY: 3, deltaMode: 1 }, anchor, canvas).y).toBe(
      -48,
    );
    expect(wheelChange(IDENTITY, { ...wheel, deltaY: 1, deltaMode: 2 }, anchor, canvas).y).toBe(
      -600,
    );
  });

  it("zooms with Ctrl and the wheel, and with a pinch on a touchpad, at the pointer", () => {
    const anchor = { x: 200, y: 100 };
    const inward = wheelChange(IDENTITY, { ...wheel, deltaY: -100, ctrlKey: true }, anchor, canvas);
    const outward = wheelChange(IDENTITY, { ...wheel, deltaY: 400, ctrlKey: true }, anchor, canvas);

    expect(inward.scale).toBeCloseTo(Math.SQRT2, 6);
    expect(toScreen(inward, toContent(IDENTITY, anchor))).toEqual(anchor);
    // A mouse's big steps count as no more than 100 pixels.
    expect(outward.scale).toBeCloseTo(Math.SQRT1_2, 6);
  });

  it("scales with two fingers around the point between them, and moves with it", () => {
    const pinched = pinchChange(
      IDENTITY,
      [
        { x: 100, y: 100 },
        { x: 200, y: 100 },
      ],
      [
        { x: 60, y: 120 },
        { x: 260, y: 120 },
      ],
    );

    expect(pinched.scale).toBe(2);
    // The content that was between the fingers is between them still.
    expect(toScreen(pinched, { x: 150, y: 100 })).toEqual({ x: 160, y: 120 });
  });

  it("reads + - 0 1 as zoom keys, not with Ctrl", () => {
    const key = (value: string, ctrlKey = false) =>
      zoomKey({ key: value, ctrlKey, metaKey: false, altKey: false });

    expect(["+", "=", "-", "_", "0", "1", "2"].map((value) => key(value))).toEqual([
      "in",
      "in",
      "out",
      "out",
      "actual",
      "fit",
      null,
    ]);
    expect(key("+", true)).toBeNull();
  });
});
