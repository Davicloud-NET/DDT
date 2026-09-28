// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useRef, useState, type KeyboardEvent, type RefObject } from "react";

import { flowCommand, flowTarget } from "@/sequences/flow/flowKeyboard";
import type { FlowBox, FlowLayout } from "@/sequences/flow/flowGeometry";
import type { TreeIndex } from "@/sequences/flow/flowTree";
import type { FlowViewportHandle } from "@/ui/FlowViewport";

export type NodeFocus = ReturnType<typeof useNodeFocus>;

export type NodeHandlers = ReturnType<NodeFocus["handlers"]>;

// The node chosen on the run's flow, the roving tab stop, and the keys that follow the flow; the node the run is at
// is chosen until the person picks another.
export function useNodeFocus(
  layout: FlowLayout,
  index: TreeIndex,
  currentId: string | null,
  viewport: RefObject<FlowViewportHandle | null>,
) {
  const [picked, setPicked] = useState<string | null>(null);
  const nodes = useRef(new Map<string, HTMLElement>());
  // A node the pointer chose is on the screen already, so only a node the keys went to is brought into view.
  const pointing = useRef(false);
  const selectedId = picked ?? currentId;
  const tabbableId =
    selectedId !== null && layout.boxes.find((box) => box.id === selectedId) !== undefined
      ? selectedId
      : null;
  const tabStopId = tabbableId ?? layout.boxes[0]?.id ?? null;

  const choose = (id: string, focus: boolean) => {
    setPicked(id);

    if (focus) {
      nodes.current.get(id)?.focus({ preventScroll: true });
    }
  };

  const keyDown = (event: KeyboardEvent, id: string) => {
    const command = flowCommand(event);

    if (command?.type !== "move") {
      return;
    }

    const target = flowTarget(index, id, command.move);

    if (target === null && command.move === "parent") {
      return;
    }

    event.preventDefault();

    if (target !== null) {
      choose(target, true);
    }
  };

  const handlers = (box: FlowBox) => ({
    ref: (element: HTMLElement | null) => {
      if (element === null) {
        nodes.current.delete(box.id);
      } else {
        nodes.current.set(box.id, element);
      }
    },
    onClick: () => {
      pointing.current = false;
      choose(box.id, false);
    },
    onPointerDown: () => {
      pointing.current = true;
    },
    onFocus: () => {
      if (pointing.current) {
        pointing.current = false;
      } else {
        viewport.current?.reveal(box);
      }
    },
    onKeyDown: (event: KeyboardEvent) => {
      if (event.key === "Enter" || event.key === " ") {
        event.preventDefault();
        choose(box.id, false);
        return;
      }

      keyDown(event, box.id);
    },
  });

  return { selectedId, tabStopId, handlers };
}
