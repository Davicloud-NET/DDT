// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useRef, type KeyboardEvent } from "react";

import type { FlowViewportHandle } from "@/ui/FlowViewport";
import type { ViewRect } from "@/ui/viewTransform";

import type { FlowLayout } from "../../flow/flowGeometry";
import { flowCommand, flowTarget, type FlowCommand } from "../../flow/flowKeyboard";
import { layoutFlow } from "../../flow/flowLayout";
import type { TreeIndex } from "../../flow/flowTree";
import type { SequenceStep } from "../../sequences";
import type { FlowDrag } from "../flowDrag";
import { useCanvasMenus } from "./useCanvasMenus";
import { useCanvasReveal, type FlowReveal } from "./useCanvasReveal";
import { useDraggedNode } from "./useDraggedNode";

interface FlowCanvasInput {
  steps: SequenceStep[];
  index: TreeIndex;
  layout?: FlowLayout;
  selectedId: string | null;
  reveal: FlowReveal | null;
  collapsed: ReadonlySet<string>;
  locked: boolean;
  drag: FlowDrag;
  addAfter: string | null;
  onAddAfterDone: () => void;
  onSelect: (id: string) => void;
  onCommand: (command: FlowCommand, id: string) => void;
}

// The canvas's state besides its layout: the elements of its nodes and slots, its menus, the roving focus and the
// node keys.
export function useFlowCanvas(input: FlowCanvasInput) {
  const { steps, index, selectedId, collapsed, locked, onSelect, onCommand } = input;
  const layout = input.layout ?? layoutFlow(steps, { collapsed });
  const viewport = useRef<FlowViewportHandle>(null);
  const nodes = useRef(new Map<string, HTMLElement>());
  const slots = useRef(new Map<string, HTMLButtonElement>());
  const canMove = useDraggedNode(input.drag, index);

  useCanvasReveal(input.reveal, layout, viewport, nodes);

  const menus = useCanvasMenus(input.addAfter, index, input.onAddAfterDone, slots);
  const tabbableId =
    selectedId !== null &&
    index.byId.has(selectedId) &&
    layout.boxes.some((box) => box.id === selectedId)
      ? selectedId
      : (steps[0]?.id ?? null);

  const keyDown = (event: KeyboardEvent, id: string) => {
    const command = flowCommand(event);

    if (command === null) {
      return;
    }

    if (command.type === "move") {
      const target = flowTarget(index, id, command.move, collapsed);

      // Escape at the top level has nowhere to go, so the key is left to the page.
      if (target === null && command.move === "parent") {
        return;
      }

      event.preventDefault();

      if (target !== null) {
        onSelect(target);
        nodes.current.get(target)?.focus({ preventScroll: true });
      }

      return;
    }

    if (command.type === "menu") {
      event.preventDefault();
      menus.openNodeMenu(id, nodes.current.get(id) ?? null);
      return;
    }

    if (locked && command.type !== "open" && command.type !== "copy") {
      return;
    }

    event.preventDefault();
    onCommand(command, id);
  };

  return {
    layout,
    viewport,
    menus,
    tabbableId,
    canMove,
    keyDown,
    // Brings what the keyboard reached into view, moving as little as it takes.
    revealRect: (rect: ViewRect) => {
      viewport.current?.reveal(rect);
    },
    registerNode: (id: string) => (element: HTMLElement | null) => {
      if (element === null) {
        nodes.current.delete(id);
      } else {
        nodes.current.set(id, element);
      }
    },
    registerSlot: (key: string) => (element: HTMLButtonElement | null) => {
      if (element === null) {
        slots.current.delete(key);
      } else {
        slots.current.set(key, element);
      }
    },
  };
}
