// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useEffect, useImperativeHandle, useRef, type Ref, type RefObject } from "react";

import type { FlowViewportHandle } from "./FlowViewport";
import {
  centerOn,
  ensureVisible,
  type ViewRect,
  type ViewSize,
  type ViewTransform,
} from "./viewTransform";

// Answers a page's FlowViewportHandle.reveal.
export function useRevealHandle(
  handle: Ref<FlowViewportHandle> | undefined,
  size: ViewSize | null,
  latest: RefObject<ViewTransform>,
  change: (next: ViewTransform) => void,
) {
  // A part asked for before the canvas knows its size is shown once it does.
  const pending = useRef<{ rect: ViewRect; center: boolean } | null>(null);

  const show = (rect: ViewRect, center: boolean, canvas: ViewSize) => {
    change(
      center
        ? centerOn(latest.current, { x: rect.x + rect.w / 2, y: rect.y + rect.h / 2 }, canvas)
        : ensureVisible(latest.current, rect, canvas),
    );
  };

  useImperativeHandle(handle, () => ({
    reveal: (rect, center = false) => {
      if (size === null) {
        pending.current = { rect, center };
      } else {
        show(rect, center, size);
      }
    },
  }));

  useEffect(() => {
    const asked = pending.current;

    if (asked !== null && size !== null) {
      pending.current = null;
      show(asked.rect, asked.center, size);
    }
    // Only a size, once known, shows what was asked for before it.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [size]);
}
