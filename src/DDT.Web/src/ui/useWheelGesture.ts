// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useEffect, useRef, type RefObject } from "react";

import { wheelChange, type ViewSize, type ViewTransform } from "./viewTransform";

// Moves the canvas with the wheel, and zooms it with Ctrl and the wheel or a touchpad's pinch.
export function useWheelGesture(
  root: RefObject<HTMLElement | null>,
  size: ViewSize | null,
  latest: RefObject<ViewTransform>,
  change: (next: ViewTransform) => void,
) {
  // The wheel is not passive, so it moves the canvas rather than the page.
  const wheel = useRef<(event: WheelEvent) => void>(() => undefined);

  useEffect(() => {
    wheel.current = (event: WheelEvent) => {
      const element = root.current;

      if (element === null || size === null) {
        return;
      }

      event.preventDefault();

      const bounds = element.getBoundingClientRect();

      change(
        wheelChange(
          latest.current,
          event,
          { x: event.clientX - bounds.left, y: event.clientY - bounds.top },
          size,
        ),
      );
    };
  });

  useEffect(() => {
    const element = root.current;

    if (element === null) {
      return;
    }

    const onWheel = (event: WheelEvent) => {
      wheel.current(event);
    };

    element.addEventListener("wheel", onWheel, { passive: false });

    return () => {
      element.removeEventListener("wheel", onWheel);
    };
  }, [root]);
}
