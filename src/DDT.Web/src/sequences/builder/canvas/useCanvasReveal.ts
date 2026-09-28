// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useEffect, type RefObject } from "react";

import type { FlowViewportHandle } from "@/ui/FlowViewport";

import type { FlowLayout } from "../../flow/flowGeometry";

// Asks the canvas to show a node, and to give it the focus unless the page takes that elsewhere.
export interface FlowReveal {
  id: string;
  focus: boolean;
  center: boolean;
  // Counts up, so asking for the same node twice shows it twice.
  count: number;
}

// Shows the node each new request names, and focuses it where the request asks.
export function useCanvasReveal(
  reveal: FlowReveal | null,
  layout: FlowLayout,
  viewport: RefObject<FlowViewportHandle | null>,
  nodes: RefObject<Map<string, HTMLElement>>,
): void {
  useEffect(() => {
    if (reveal === null) {
      return;
    }

    const box = layout.boxes.find((candidate) => candidate.id === reveal.id);

    if (box !== undefined) {
      viewport.current?.reveal(box, reveal.center);
    }

    if (!reveal.focus) {
      return;
    }

    // A menu that closes keeps the rest of the page out of reach until it is gone, so the focus is given again on the
    // next frames until the node has it.
    let frame = 0;
    let tries = 0;
    const give = () => {
      const element = nodes.current.get(reveal.id);

      element?.focus({ preventScroll: true });

      if (element !== undefined && document.activeElement !== element && tries++ < 10) {
        frame = requestAnimationFrame(give);
      }
    };

    frame = requestAnimationFrame(give);

    return () => {
      cancelAnimationFrame(frame);
    };
    // Only a new request moves the view; a new layout alone does not.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [reveal]);
}
