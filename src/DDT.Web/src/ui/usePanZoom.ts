// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useEffect, useRef, useState } from "react";

import {
  clampPan,
  fitTransform,
  IDENTITY,
  topTransform,
  zoomAt,
  zoomBy,
  ZOOM_STEP,
  type ViewPoint,
  type ViewSize,
  type ViewTransform,
  type ZoomCommand,
} from "./viewTransform";

interface PanZoomOptions {
  content: ViewSize;
  // The canvas's size, null until it is measured.
  size: ViewSize | null;
  // The page's transform, when it keeps one.
  transform: ViewTransform | undefined;
  onTransformChange: ((next: ViewTransform) => void) | undefined;
  start: "fit" | "top";
}

// The transform a canvas shows: the page's when it passes one, else the canvas's own, starting fitted or at the top.
// Every change is clamped, so some of the content always stays in view.
export function usePanZoom({ content, size, transform, onTransformChange, start }: PanZoomOptions) {
  const [own, setOwn] = useState<ViewTransform | null>(null);
  const view =
    transform ??
    own ??
    (size === null
      ? IDENTITY
      : start === "top"
        ? clampPan(topTransform(content, size), content, size)
        : fitTransform(content, size));
  // The newest transform, for events that come faster than the page renders, such as the wheel's.
  const latest = useRef(view);

  useEffect(() => {
    latest.current = view;
  });

  const change = (next: ViewTransform) => {
    const kept = size === null ? next : clampPan(next, content, size);

    latest.current = kept;

    if (transform === undefined) {
      setOwn(kept);
    }

    onTransformChange?.(kept);
  };

  const middle = (): ViewPoint =>
    size === null ? { x: 0, y: 0 } : { x: size.width / 2, y: size.height / 2 };

  const zoom = (command: ZoomCommand) => {
    switch (command) {
      case "in":
        change(zoomBy(latest.current, ZOOM_STEP, middle()));
        break;
      case "out":
        change(zoomBy(latest.current, 1 / ZOOM_STEP, middle()));
        break;
      case "actual":
        change(zoomAt(latest.current, 1, middle()));
        break;
      case "fit":
        if (size !== null) {
          change(fitTransform(content, size));
        }
        break;
    }
  };

  return { view, latest, change, zoom };
}
