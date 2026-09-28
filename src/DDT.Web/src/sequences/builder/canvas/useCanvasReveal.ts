// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useEffect, type RefObject } from "react";

import type { FlowViewportHandle } from "@/ui/FlowViewport";

import type { FlowLayout } from "../../flow/flowGeometry";

// Asks the canvas to show a node and focus it, unless the page moves the focus somewhere else.
export interface FlowReveal {
  id: string;
  focus: boolean;
  center: boolean;
  // Counts up, so asking for the same node twice shows it twice.
  count: number;
}

// Shows the node each new request names, and focuses it if the request asks for that.
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

    // A closing menu keeps the rest of the page out of reach until it's gone. So the focus is set again on the next
    // frames until the node has it.
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
    // Only a new request moves the view. A new layout alone doesn't.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [reveal]);
}
