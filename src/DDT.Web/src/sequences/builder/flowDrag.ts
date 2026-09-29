// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { isTextDropItem, type DropItem } from "react-aria-components";

import { isWithin, type Slot, type TreeIndex } from "../flow/flowTree";
import type { StepKind } from "../sequences";

// What a drag over the flow carries: a kind from the palette, or a node of the flow.
export const KIND_TYPE = "application/x-ddt-flow-kind";
export const NODE_TYPE = "application/x-ddt-flow-node";

export type FlowDrag = { kind: StepKind } | { node: string } | null;

// Whether moving the node to the slot changes anything and keeps the tree a tree.
export function canMoveTo(index: TreeIndex, id: string, slot: Slot): boolean {
  const entry = index.byId.get(id);

  if (entry === undefined || (slot.parent !== null && isWithin(index, slot.parent, id))) {
    return false;
  }

  return !(
    slot.parent === entry.parent &&
    slot.body === entry.body &&
    (slot.index === entry.index || slot.index === entry.index + 1)
  );
}

export async function droppedText(
  items: readonly DropItem[],
  type: string,
): Promise<string | null> {
  for (const item of items) {
    if (isTextDropItem(item) && item.types.has(type)) {
      return item.getText(type);
    }
  }

  return null;
}
