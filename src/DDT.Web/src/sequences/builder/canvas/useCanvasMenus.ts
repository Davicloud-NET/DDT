// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useEffect, useRef, useState, type RefObject } from "react";

import type { Slot, TreeIndex } from "../../flow/flowTree";
import { slotId } from "./slotId";

// The canvas's two menus: a slot's kinds to add, and a node's actions. Each opens from the element it belongs to.
// addAfter opens the kinds of the gap after a node, when that node's menu asks for it.
export function useCanvasMenus(
  addAfter: string | null,
  index: TreeIndex,
  onAddAfterDone: () => void,
  slots: RefObject<Map<string, HTMLButtonElement>>,
) {
  const [menuSlot, setMenuSlot] = useState<Slot | null>(null);
  const slotTrigger = useRef<HTMLElement | null>(null);
  const [menuNode, setMenuNode] = useState<string | null>(null);
  const nodeTrigger = useRef<HTMLElement | null>(null);

  useEffect(() => {
    if (addAfter === null) {
      return;
    }

    const entry = index.byId.get(addAfter);

    onAddAfterDone();

    if (entry === undefined) {
      return;
    }

    const slot = { parent: entry.parent, body: entry.body, index: entry.index + 1 };
    const element = slots.current.get(slotId(slot));

    if (element !== undefined) {
      slotTrigger.current = element;
      // The menu needs the gap's element as its trigger, and only the rendered canvas has it.
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setMenuSlot(slot);
    }
  }, [addAfter, index, onAddAfterDone, slots]);

  return {
    menuSlot,
    slotTrigger,
    openSlotMenu: (slot: Slot, element: HTMLElement) => {
      slotTrigger.current = element;
      setMenuSlot(slot);
    },
    closeSlotMenu: () => {
      setMenuSlot(null);
    },
    menuNode,
    nodeTrigger,
    openNodeMenu: (id: string, element: HTMLElement | null) => {
      nodeTrigger.current = element;
      setMenuNode(id);
    },
    closeNodeMenu: () => {
      setMenuNode(null);
    },
  };
}
